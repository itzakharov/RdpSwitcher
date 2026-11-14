using System;
using System.Drawing;
using RdpSwitcher.Config;
using RdpSwitcher.UI;
using Xunit;

namespace RdpSwitcher.Tests
{
    public class EdgePanelLogicTests
    {
        // A monitor that does not start at 0,0 to catch missing offsets.
        private static readonly Rectangle Area = new Rectangle(100, 50, 1920, 1080);

        private static PanelSettings Settings(PanelEdge edge = PanelEdge.Top, int show = 150, int hide = 600)
        {
            return new PanelSettings { Edge = edge, Size = 44, TriggerZone = 4, ShowDelayMs = show, HideDelayMs = hide };
        }

        private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

        // Cursor in the middle of the trigger strip for an edge.
        private static Point InTrigger(PanelEdge edge)
        {
            switch (edge)
            {
                case PanelEdge.Bottom: return new Point(1000, Area.Bottom - 1);
                case PanelEdge.Left: return new Point(Area.Left, 600);
                case PanelEdge.Right: return new Point(Area.Right - 1, 600);
                default: return new Point(1000, Area.Top);
            }
        }

        private static readonly Point FarAway = new Point(1000, 600);

        [Theory]
        [InlineData(PanelEdge.Top, 100, 50, 1920, 44)]
        [InlineData(PanelEdge.Bottom, 100, 1086, 1920, 44)]
        [InlineData(PanelEdge.Left, 100, 50, 44, 1080)]
        [InlineData(PanelEdge.Right, 1976, 50, 44, 1080)]
        public void ComputeBounds_PerEdge(PanelEdge edge, int x, int y, int w, int h)
        {
            Assert.Equal(new Rectangle(x, y, w, h), EdgePanelLogic.ComputeBounds(Area, edge, 44));
        }

        [Theory]
        [InlineData(PanelEdge.Top, 100, 50, 1920, 4)]
        [InlineData(PanelEdge.Bottom, 100, 1126, 1920, 4)]
        [InlineData(PanelEdge.Left, 100, 50, 4, 1080)]
        [InlineData(PanelEdge.Right, 2016, 50, 4, 1080)]
        public void ComputeTriggerZone_PerEdge(PanelEdge edge, int x, int y, int w, int h)
        {
            Assert.Equal(new Rectangle(x, y, w, h), EdgePanelLogic.ComputeTriggerZone(Area, edge, 4));
        }

        [Fact]
        public void IsHorizontal_OnlyForTopAndBottom()
        {
            Assert.True(new EdgePanelLogic(Settings(PanelEdge.Top)).IsHorizontal);
            Assert.True(new EdgePanelLogic(Settings(PanelEdge.Bottom)).IsHorizontal);
            Assert.False(new EdgePanelLogic(Settings(PanelEdge.Left)).IsHorizontal);
            Assert.False(new EdgePanelLogic(Settings(PanelEdge.Right)).IsHorizontal);
        }

        [Theory]
        [InlineData(PanelEdge.Top)]
        [InlineData(PanelEdge.Bottom)]
        [InlineData(PanelEdge.Left)]
        [InlineData(PanelEdge.Right)]
        public void Show_AfterCursorStaysInTriggerForShowDelay(PanelEdge edge)
        {
            var logic = new EdgePanelLogic(Settings(edge));
            var cursor = InTrigger(edge);

            Assert.Equal(PanelAction.None, logic.Update(cursor, Area, Ms(0)));
            Assert.Equal(PanelAction.None, logic.Update(cursor, Area, Ms(149)));
            Assert.False(logic.Visible);
            Assert.Equal(PanelAction.Show, logic.Update(cursor, Area, Ms(150)));
            Assert.True(logic.Visible);
        }

        [Fact]
        public void Show_IsCancelledWhenCursorLeavesBeforeDelay()
        {
            var logic = new EdgePanelLogic(Settings());
            var inside = InTrigger(PanelEdge.Top);

            logic.Update(inside, Area, Ms(0));
            logic.Update(FarAway, Area, Ms(100));
            logic.Update(inside, Area, Ms(120));

            // Countdown restarted at 120, so 200 is only 80 ms after re-entering.
            Assert.Equal(PanelAction.None, logic.Update(inside, Area, Ms(200)));
            Assert.Equal(PanelAction.Show, logic.Update(inside, Area, Ms(270)));
        }

        [Fact]
        public void Show_DoesNotHappenOutsideTriggerStrip()
        {
            var logic = new EdgePanelLogic(Settings());
            var justBelow = new Point(1000, Area.Top + 4);

            logic.Update(justBelow, Area, Ms(0));

            Assert.Equal(PanelAction.None, logic.Update(justBelow, Area, Ms(10000)));
            Assert.False(logic.Visible);
        }

        [Fact]
        public void Show_ZeroDelayShowsOnFirstUpdate()
        {
            var logic = new EdgePanelLogic(Settings(show: 0));

            Assert.Equal(PanelAction.Show, logic.Update(InTrigger(PanelEdge.Top), Area, Ms(0)));
        }

        private static EdgePanelLogic ShownPanel(PanelEdge edge = PanelEdge.Top, int hide = 600)
        {
            var logic = new EdgePanelLogic(Settings(edge, show: 0, hide: hide));
            logic.Update(InTrigger(edge), Area, Ms(0));
            Assert.True(logic.Visible);
            return logic;
        }

        [Fact]
        public void Hide_AfterCursorStaysOutsideForHideDelay()
        {
            var logic = ShownPanel();

            Assert.Equal(PanelAction.None, logic.Update(FarAway, Area, Ms(1000)));
            Assert.Equal(PanelAction.None, logic.Update(FarAway, Area, Ms(1599)));
            Assert.True(logic.Visible);
            Assert.Equal(PanelAction.Hide, logic.Update(FarAway, Area, Ms(1600)));
            Assert.False(logic.Visible);
        }

        [Fact]
        public void Hide_IsCancelledWhenCursorReturns()
        {
            var logic = ShownPanel();

            logic.Update(FarAway, Area, Ms(1000));
            logic.Update(InTrigger(PanelEdge.Top), Area, Ms(1400));

            // Left again at 1500: the countdown starts anew.
            Assert.Equal(PanelAction.None, logic.Update(FarAway, Area, Ms(1500)));
            Assert.Equal(PanelAction.None, logic.Update(FarAway, Area, Ms(2000)));
            Assert.Equal(PanelAction.Hide, logic.Update(FarAway, Area, Ms(2100)));
        }

        [Fact]
        public void Hide_NotWhileCursorIsOverThePanel()
        {
            var logic = ShownPanel();
            var overPanel = new Point(1000, Area.Top + 30);

            logic.Update(overPanel, Area, Ms(0));

            Assert.Equal(PanelAction.None, logic.Update(overPanel, Area, Ms(60000)));
            Assert.True(logic.Visible);
        }

        [Fact]
        public void Hide_ZeroDelayHidesOnFirstOutsideUpdate()
        {
            var logic = ShownPanel(hide: 0);

            Assert.Equal(PanelAction.Hide, logic.Update(FarAway, Area, Ms(10)));
        }

        [Fact]
        public void Pinned_PanelNeverHides()
        {
            var logic = ShownPanel();
            logic.Pinned = true;

            Assert.Equal(PanelAction.None, logic.Update(FarAway, Area, Ms(0)));
            Assert.Equal(PanelAction.None, logic.Update(FarAway, Area, Ms(60000)));
            Assert.True(logic.Visible);
        }

        [Fact]
        public void Pinned_ShowsHiddenPanelWithoutCursor()
        {
            var logic = new EdgePanelLogic(Settings());
            logic.Pinned = true;

            Assert.Equal(PanelAction.Show, logic.Update(FarAway, Area, Ms(0)));
            Assert.True(logic.Visible);
        }

        [Fact]
        public void Unpin_StartsNormalHideCountdown()
        {
            var logic = ShownPanel();
            logic.Pinned = true;
            logic.Update(FarAway, Area, Ms(5000));
            logic.Pinned = false;

            Assert.Equal(PanelAction.None, logic.Update(FarAway, Area, Ms(6000)));
            Assert.Equal(PanelAction.Hide, logic.Update(FarAway, Area, Ms(6600)));
        }

        [Fact]
        public void Apply_ChangesEdge_TriggerMovesToNewEdge()
        {
            var logic = ShownPanel(PanelEdge.Top);
            logic.Update(FarAway, Area, Ms(1000));
            logic.Apply(Settings(PanelEdge.Left, show: 0));

            Assert.Equal(PanelEdge.Left, logic.Edge);
            Assert.Equal(new Rectangle(Area.Left, Area.Top, 44, Area.Height), logic.Bounds(Area));
            Assert.True(logic.Visible);

            // The countdown restarted on Apply: the old outside time does not count.
            Assert.Equal(PanelAction.None, logic.Update(FarAway, Area, Ms(1300)));
            // Cursor at the old (top) edge no longer keeps the panel alive.
            Assert.Equal(PanelAction.Hide, logic.Update(InTrigger(PanelEdge.Top), Area, Ms(1900)));
        }

        [Fact]
        public void Apply_NewSettingsAreUsed()
        {
            var logic = new EdgePanelLogic(Settings());
            logic.Apply(new PanelSettings { Edge = PanelEdge.Bottom, Size = 60, TriggerZone = 2, ShowDelayMs = 10, HideDelayMs = 20 });

            Assert.Equal(60, logic.Size);
            Assert.Equal(2, logic.TriggerZone);
            Assert.Equal(PanelAction.None, logic.Update(InTrigger(PanelEdge.Bottom), Area, Ms(0)));
            Assert.Equal(PanelAction.Show, logic.Update(InTrigger(PanelEdge.Bottom), Area, Ms(10)));
        }

        [Fact]
        public void Show_IgnoresCursorOnAnotherMonitor()
        {
            var logic = new EdgePanelLogic(Settings(show: 0));
            var otherMonitorTop = new Point(-500, Area.Top);

            Assert.Equal(PanelAction.None, logic.Update(otherMonitorTop, Area, Ms(0)));
        }
    }
}
