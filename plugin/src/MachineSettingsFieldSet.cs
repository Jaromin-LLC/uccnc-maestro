using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Plugins
{
    /// <summary>Probe + tool-change coordinate fields used in Admin (global and per-project).</summary>
    public class MachineSettingsFieldSet
    {
        public TextBox PlateX;
        public TextBox PlateY;
        public TextBox ProbeDist;
        public TextBox RetractDist;
        public TextBox FeedFast;
        public TextBox FeedSlow;
        public TextBox PlateRapidZ;
        public TextBox PlateZero;
        public TextBox TcX;
        public TextBox TcY;
        public TextBox TcZ;
        public TextBox SafeZ;
        public CheckBox UseSafeZ;
        public CheckBox PlateRapid;

        public static MachineSettingsFieldSet Create(Panel parent, ref int y, ToolTip tips)
        {
            var f = new MachineSettingsFieldSet();

            AddRow(parent, ref y, tips, "Plate X", out f.PlateX,
                "Machine X (G53) of the fixed tool setter / touch plate center.");
            AddRow(parent, ref y, tips, "Plate Y", out f.PlateY,
                "Machine Y (G53) of the fixed tool setter / touch plate center.");
            AddRow(parent, ref y, tips, "Probe distance", out f.ProbeDist,
                "Maximum Z travel for each G31 probe move downward. Set slightly larger than the gap from the probe start height to the plate.");
            AddRow(parent, ref y, tips, "Retract distance", out f.RetractDist,
                "Z lift after the fast probe touch, before the second slow probe pass.");
            AddRow(parent, ref y, tips, "Fast probe feed", out f.FeedFast,
                "Feed rate for the first (fast) probe pass.");
            AddRow(parent, ref y, tips, "Slow probe feed", out f.FeedSlow,
                "Feed rate for the second (precise) probe pass.");
            AddRow(parent, ref y, tips, "Plate rapid Z", out f.PlateRapidZ,
                "Machine Z to rapid to before probing when \"Rapid to plate Z\" is enabled.");
            AddRow(parent, ref y, tips, "Plate offset from Z zero", out f.PlateZero,
                "Height of the plate top above your work Z0. When the tool touches the plate top, work Z is set to this value. " +
                "With the puck sitting on the machine base and Z0 at the base, this equals the plate (puck) thickness; use 0 to zero directly on the plate top.");
            AddRow(parent, ref y, tips, "Tool change X", out f.TcX,
                "Machine X where the spindle parks for manual tool changes.");
            AddRow(parent, ref y, tips, "Tool change Y", out f.TcY,
                "Machine Y where the spindle parks for manual tool changes.");
            AddRow(parent, ref y, tips, "Tool change Z", out f.TcZ,
                "Machine Z at the tool change position (tool tip height at the change location).");
            AddRow(parent, ref y, tips, "Safe Z", out f.SafeZ,
                "Machine Z used for safe retract moves before traveling in XY to tool change or probe.");

            f.UseSafeZ = new CheckBox
            {
                Text = "Retract to Safe Z before tool-change / probe moves",
                Location = new Point(12, y),
                AutoSize = true
            };
            tips.SetToolTip(f.UseSafeZ,
                "When enabled, the machine moves to Safe Z before any XY travel to the tool change position or probe plate.");
            parent.Controls.Add(f.UseSafeZ);
            y += 26;

            f.PlateRapid = new CheckBox
            {
                Text = "Rapid to plate Z before probing",
                Location = new Point(12, y),
                AutoSize = true
            };
            tips.SetToolTip(f.PlateRapid,
                "When enabled, rapids to Plate rapid Z before starting the probe sequence.");
            parent.Controls.Add(f.PlateRapid);
            y += 28;

            return f;
        }

        private static void AddRow(Panel parent, ref int y, ToolTip tips, string label, out TextBox box, string tip)
        {
            var lbl = MkLabel(label, 12, y);
            parent.Controls.Add(lbl);
            box = MkText(140, y, 100);
            parent.Controls.Add(box);
            tips.SetToolTip(box, tip);
            tips.SetToolTip(lbl, tip);
            y += 28;
        }

        private static Label MkLabel(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y + 3), AutoSize = true };
        }

        private static TextBox MkText(int x, int y, int width)
        {
            return new TextBox { Location = new Point(x, y), Width = width };
        }

        public void LoadFrom(ProbeSettings probe, ToolChangePos tc, bool useSafeZ)
        {
            if (probe == null) probe = new ProbeSettings();
            if (tc == null) tc = new ToolChangePos();
            PlateX.Text = Fmt(probe.xPlate);
            PlateY.Text = Fmt(probe.yPlate);
            ProbeDist.Text = Fmt(probe.dist);
            RetractDist.Text = Fmt(probe.retractDist);
            FeedFast.Text = Fmt(probe.feedFast);
            FeedSlow.Text = Fmt(probe.feedSlow);
            PlateRapidZ.Text = Fmt(probe.plateRapidZ);
            PlateZero.Text = Fmt(probe.plateZero);
            TcX.Text = Fmt(tc.x);
            TcY.Text = Fmt(tc.y);
            TcZ.Text = Fmt(tc.z);
            SafeZ.Text = Fmt(tc.zSafe);
            UseSafeZ.Checked = useSafeZ;
            PlateRapid.Checked = probe.plateRapid;
        }

        public void SaveTo(ProbeSettings probe, ToolChangePos tc, ref bool useSafeZ)
        {
            if (probe == null) throw new ArgumentNullException("probe");
            if (tc == null) throw new ArgumentNullException("tc");
            probe.xPlate = Parse(PlateX);
            probe.yPlate = Parse(PlateY);
            probe.dist = Parse(ProbeDist);
            probe.retractDist = Parse(RetractDist);
            probe.feedFast = Parse(FeedFast);
            probe.feedSlow = Parse(FeedSlow);
            probe.plateRapidZ = Parse(PlateRapidZ);
            probe.plateZero = Parse(PlateZero);
            probe.plateRapid = PlateRapid.Checked;
            tc.x = Parse(TcX);
            tc.y = Parse(TcY);
            tc.z = Parse(TcZ);
            tc.zSafe = Parse(SafeZ);
            useSafeZ = UseSafeZ.Checked;
        }

        public void SetEnabled(bool enabled)
        {
            foreach (Control c in AllControls())
                c.Enabled = enabled;
        }

        private Control[] AllControls()
        {
            return new Control[]
            {
                PlateX, PlateY, ProbeDist, RetractDist, FeedFast, FeedSlow,
                PlateRapidZ, PlateZero, TcX, TcY, TcZ, SafeZ, UseSafeZ, PlateRapid
            };
        }

        public void HookChanged(EventHandler handler)
        {
            PlateX.TextChanged += handler;
            PlateY.TextChanged += handler;
            ProbeDist.TextChanged += handler;
            RetractDist.TextChanged += handler;
            FeedFast.TextChanged += handler;
            FeedSlow.TextChanged += handler;
            PlateRapidZ.TextChanged += handler;
            PlateZero.TextChanged += handler;
            TcX.TextChanged += handler;
            TcY.TextChanged += handler;
            TcZ.TextChanged += handler;
            SafeZ.TextChanged += handler;
            UseSafeZ.CheckedChanged += handler;
            PlateRapid.CheckedChanged += handler;
        }

        private static string Fmt(double v)
        {
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static double Parse(TextBox box)
        {
            if (box == null) return 0;
            double v;
            return double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
