using System;
using System.Drawing;
using RdpSwitcher.Config;

namespace RdpSwitcher.UI
{
    public enum PanelAction
    {
        None,
        Show,
        Hide
    }

    /// <summary>
    /// Pure logic of the edge panel (no controls, only System.Drawing): where the panel and its trigger strip are,
    /// and when it should appear or disappear based on the cursor position polled by a timer.
    /// All coordinates are physical screen pixels; <c>area</c> is the screen rectangle the panel lives in.
    /// </summary>
    public class EdgePanelLogic
    {
        private TimeSpan? _insideSince;
        private TimeSpan? _outsideSince;

        public EdgePanelLogic(PanelSettings settings)
        {
            Apply(settings);
        }

        public PanelEdge Edge { get; private set; }
        public int Size { get; private set; }
        public int TriggerZone { get; private set; }
        public int ShowDelayMs { get; private set; }
        public int HideDelayMs { get; private set; }

        /// <summary>True while the panel is shown (the caller applies Show/Hide results to the real control).</summary>
        public bool Visible { get; private set; }

        /// <summary>Pinned: the panel is shown and never hides. Unpinning starts the normal hide countdown.</summary>
        public bool Pinned { get; set; }

        /// <summary>Takes new settings (edge, size, delays) and restarts the delay countdowns; Visible and Pinned are kept.</summary>
        public void Apply(PanelSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            Edge = settings.Edge;
            Size = settings.Size;
            TriggerZone = settings.TriggerZone;
            ShowDelayMs = settings.ShowDelayMs;
            HideDelayMs = settings.HideDelayMs;
            ResetTimers();
        }

        public static Rectangle ComputeBounds(Rectangle area, PanelEdge edge, int size)
        {
            return Strip(area, edge, size);
        }

        public static Rectangle ComputeTriggerZone(Rectangle area, PanelEdge edge, int triggerZone)
        {
            return Strip(area, edge, triggerZone);
        }

        /// <summary>The part of <paramref name="area"/> that stays free when the panel strip is reserved (never negative in size).</summary>
        public static Rectangle ComputeRemaining(Rectangle area, PanelEdge edge, int size)
        {
            var horizontal = edge == PanelEdge.Top || edge == PanelEdge.Bottom;
            var cut = Math.Max(0, Math.Min(size, horizontal ? area.Height : area.Width));
            switch (edge)
            {
                case PanelEdge.Bottom: return new Rectangle(area.X, area.Y, area.Width, area.Height - cut);
                case PanelEdge.Left: return new Rectangle(area.X + cut, area.Y, area.Width - cut, area.Height);
                case PanelEdge.Right: return new Rectangle(area.X, area.Y, area.Width - cut, area.Height);
                default: return new Rectangle(area.X, area.Y + cut, area.Width, area.Height - cut);
            }
        }

        public Rectangle Bounds(Rectangle area)
        {
            return ComputeBounds(area, Edge, Size);
        }

        /// <summary>True for edges where the panel is a horizontal bar (buttons laid out left to right).</summary>
        public bool IsHorizontal => Edge == PanelEdge.Top || Edge == PanelEdge.Bottom;

        /// <summary>
        /// Call every poll tick. <paramref name="now"/> is any monotonic clock (e.g. Stopwatch.Elapsed).
        /// Returns what to do with the control; Visible is already updated.
        /// </summary>
        public PanelAction Update(Point cursor, Rectangle area, TimeSpan now)
        {
            var trigger = ComputeTriggerZone(area, Edge, TriggerZone);

            if (!Visible)
            {
                _outsideSince = null;

                if (Pinned)
                {
                    Visible = true;
                    _insideSince = null;
                    return PanelAction.Show;
                }

                if (!trigger.Contains(cursor))
                {
                    _insideSince = null;
                    return PanelAction.None;
                }

                if (_insideSince == null) _insideSince = now;
                if ((now - _insideSince.Value).TotalMilliseconds < ShowDelayMs) return PanelAction.None;

                Visible = true;
                _insideSince = null;
                return PanelAction.Show;
            }

            _insideSince = null;

            if (Pinned || trigger.Contains(cursor) || Bounds(area).Contains(cursor))
            {
                _outsideSince = null;
                return PanelAction.None;
            }

            if (_outsideSince == null) _outsideSince = now;
            if ((now - _outsideSince.Value).TotalMilliseconds < HideDelayMs) return PanelAction.None;

            Visible = false;
            _outsideSince = null;
            return PanelAction.Hide;
        }

        private void ResetTimers()
        {
            _insideSince = null;
            _outsideSince = null;
        }

        private static Rectangle Strip(Rectangle area, PanelEdge edge, int thickness)
        {
            switch (edge)
            {
                case PanelEdge.Bottom:
                    return new Rectangle(area.Left, area.Bottom - thickness, area.Width, thickness);
                case PanelEdge.Left:
                    return new Rectangle(area.Left, area.Top, thickness, area.Height);
                case PanelEdge.Right:
                    return new Rectangle(area.Right - thickness, area.Top, thickness, area.Height);
                default:
                    return new Rectangle(area.Left, area.Top, area.Width, thickness);
            }
        }
    }
}
