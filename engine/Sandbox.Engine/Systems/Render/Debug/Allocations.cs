namespace Sandbox;

internal static partial class DebugOverlay
{
	public partial class Allocations
	{
		static Sandbox.Diagnostics.Allocations.Scope _scope;
		static double _openTime = -1;
		static double _lastFlushTime = -1;
		static Dictionary<string, (long Bytes, long Count)> _accumByType = new( AccumCap );
		static List<(string Name, long Bytes, long Count)> _topAllocs = new( DisplayCount );

		const int DisplayCount = 50;
		const int AccumCap = DisplayCount * 10;

		// 2ms in TimeSpan ticks (100ns each)
		static readonly long StutterThresholdTicks = TimeSpan.FromMilliseconds( 2 ).Ticks;

		static double _elapsedSeconds = 0;
		static long _frameCount = 0;
		static long _pauseTicksSum = 0;
		static long _stutterCount = 0;
		static long _pauseTicksMin = long.MaxValue;
		static long _pauseTicksMax = 0;
		static long _gen0Sum = 0;
		static long _gen1Sum = 0;
		static long _gen2Sum = 0;
		static long _allocBytesSum = 0;

		// accumulator for the current in-flight second
		static long _curFrames = 0;
		static long _curPauseTicks = 0;
		static long _curPauseTicksMin = long.MaxValue;
		static long _curPauseTicksMax = 0;

		internal static void Disabled()
		{
			_scope?.Stop();
			_scope = null;
			_openTime = -1;
			_lastFlushTime = -1;
			_elapsedSeconds = 0;
			_frameCount = 0;
			_pauseTicksSum = 0;
			_pauseTicksMin = long.MaxValue;
			_pauseTicksMax = 0;
			_gen0Sum = 0;
			_gen1Sum = 0;
			_gen2Sum = 0;
			_allocBytesSum = 0;
			_accumByType.Clear();
			_topAllocs.Clear();
			_curFrames = 0;
			_curPauseTicks = 0;
			_curPauseTicksMin = long.MaxValue;
			_curPauseTicksMax = 0;
		}

		internal static void Draw( ref Vector2 pos )
		{
			_scope ??= new();
			_scope.Start();

			var now = RealTime.Now;
			if ( _openTime < 0 )
			{
				_openTime = now;
				_lastFlushTime = now;
			}

			var ls = Sandbox.Diagnostics.PerformanceStats.LastSecond;

			if ( now - _lastFlushTime >= 1.0 )
			{
				_lastFlushTime = now;

				foreach ( var e in _scope.Entries )
				{
					var prev = _accumByType.GetValueOrDefault( e.Name );
					_accumByType[e.Name] = (prev.Bytes + (long)e.TotalBytes, prev.Count + (long)e.Count);
					_allocBytesSum += (long)e.TotalBytes;
				}

				_topAllocs.Clear();
				foreach ( var kv in _accumByType.OrderByDescending( x => x.Value.Bytes ).Take( DisplayCount ) )
					_topAllocs.Add( (kv.Key, kv.Value.Bytes, kv.Value.Count) );

				if ( _accumByType.Count > AccumCap )
				{
					var toRemove = _accumByType.OrderBy( x => x.Value.Bytes ).Take( _accumByType.Count - AccumCap ).Select( x => x.Key ).ToList();
					foreach ( var key in toRemove ) _accumByType.Remove( key );
				}

				_scope.Clear();

				_elapsedSeconds = now - _openTime;
				_frameCount += _curFrames;
				_pauseTicksSum += _curPauseTicks;
				_stutterCount = _scope.SlowCollectionCount;
				if ( _curPauseTicksMin < _pauseTicksMin ) _pauseTicksMin = _curPauseTicksMin;
				if ( _curPauseTicksMax > _pauseTicksMax ) _pauseTicksMax = _curPauseTicksMax;
				_gen0Sum += ls.Gc0;
				_gen1Sum += ls.Gc1;
				_gen2Sum += ls.Gc2;

				_curFrames = 0;
				_curPauseTicks = 0;
				_curPauseTicksMin = long.MaxValue;
				_curPauseTicksMax = 0;
			}

			var gcPause = Sandbox.Diagnostics.PerformanceStats.GcPause;
			_curFrames++;
			_curPauseTicks += gcPause;
			if ( gcPause < _curPauseTicksMin ) _curPauseTicksMin = gcPause;
			if ( gcPause > _curPauseTicksMax ) _curPauseTicksMax = gcPause;

			if ( _elapsedSeconds < 1.0 )
				return;

			var x = pos.x;
			var y = pos.y;

			var scope = new TextRendering.Scope( "", Color.White, 11, "Roboto Mono", 600 );
			scope.Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 };

			{
				var lowestPauseMs = TimeSpan.FromTicks( _pauseTicksMin == long.MaxValue ? 0 : _pauseTicksMin ).TotalMilliseconds;
				var highestPauseMs = TimeSpan.FromTicks( _pauseTicksMax ).TotalMilliseconds;
				var sumMs = TimeSpan.FromTicks( _pauseTicksSum ).TotalMilliseconds;
				var avgMs = _frameCount > 0 ? TimeSpan.FromTicks( _pauseTicksSum / _frameCount ).TotalMilliseconds : 0.0;

				var mbPerSec = _elapsedSeconds > 0 ? _allocBytesSum / (1024.0 * 1024.0 * _elapsedSeconds) : 0.0;
				var mbTotal = _allocBytesSum / (1024.0 * 1024.0);
				var elapsedSecondsInt = (int)_elapsedSeconds;
				var windowLabel = elapsedSecondsInt >= 60 ? $"{elapsedSecondsInt / 60}m {elapsedSecondsInt % 60}s" : $"{elapsedSecondsInt}s";
				const string Pad = "{0,-13}";
				scope.Text = $"GC Pauses (open for {windowLabel})\n" +
					string.Format( Pad, "Gen (0/1/2):" ) + $"{_gen0Sum} / {_gen1Sum} / {_gen2Sum}\n" +
					string.Format( Pad, "Total:" ) + $"{mbTotal:N1} MB\n" +
					string.Format( Pad, "Rate:" ) + $"{mbPerSec:N2} MB/s\n" +
					string.Format( Pad, "Avg:" ) + $"{avgMs:N2}ms\n" +
					string.Format( Pad, "Min:" ) + $"{lowestPauseMs:N2}ms\n" +
					string.Format( Pad, "Max:" ) + $"{highestPauseMs:N2}ms\n" +
					string.Format( Pad, "Sum:" ) + $"{sumMs:N2}ms\n" +
					string.Format( Pad, "Time in GC:" ) + $"{sumMs / (_elapsedSeconds * 1000.0) * 100.0:N2}%\n" +
					string.Format( Pad, $"Stutter >{StutterThresholdTicks / TimeSpan.TicksPerMillisecond}ms:" ) + $"{_stutterCount}";
				Hud.DrawText( scope, new Rect( x, y, 512, 13 ), TextFlag.LeftTop );

				y += 140;
			}

			y += 14;

			scope.TextColor = new Color( 1f, 1f, 0.5f );
			scope.Text = $"Top {_topAllocs.Count} Allocations";
			Hud.DrawText( scope, new Vector2( x + 20, y ), TextFlag.LeftTop );
			y += 16;

			foreach ( var e in _topAllocs )
			{
				scope.TextColor = GetLineColor( e.Name );

				{
					scope.Text = e.Bytes.FormatBytes();
					Hud.DrawText( scope, new Rect( x + 20, y, 50, 13 ), TextFlag.RightTop );
				}

				{
					scope.Text = e.Count.KiloFormat();
					Hud.DrawText( scope, new Rect( x + 20, y, 75, 13 ), TextFlag.RightTop );
				}

				{
					scope.Text = e.Name;
					Hud.DrawText( scope, new Vector2( x + 100, y ), TextFlag.LeftTop );
				}

				y += 14;
			}

			pos.y = y;
		}

		static Color GetLineColor( string name )
		{
			if ( name.StartsWith( "System." ) ) return new Color( 0.7f, 1f, 0.7f );
			if ( name.StartsWith( "<GetAll>" ) ) return new Color( 1f, 1f, 0.7f );

			return Color.White;
		}
	}
}
