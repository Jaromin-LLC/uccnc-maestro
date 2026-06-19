using System;
using System.Windows.Forms;

namespace Plugins
{
    public class UCCNCplugin
    {
        private bool _firstRun = true;
        public Plugininterface.Entry UC;
        private MaestroForm _form;
        public bool loopstop = false;
        public bool loopworking = false;

        public WorkflowEngine Engine { get; private set; }

        public UCCNCplugin()
        {
        }

        public void Init_event(Plugininterface.Entry uc)
        {
            UC = uc;
            _form = new MaestroForm(this);
            Engine = _form.Engine;
        }

        public Plugininterface.Entry.Pluginproperties Getproperties_event(Plugininterface.Entry.Pluginproperties properties)
        {
            properties.author = "Jaromin";
            properties.pluginname = "JarominMaestro";
            properties.pluginversion = "1.0.0";
            return properties;
        }

        public void Configure_event()
        {
            MessageBox.Show(
                "Jaromin Maestro\nBuild: " + BuildInfo.Id +
                "\n\nConfiguration is stored in:\n" + MaestroPaths.ProjectsFile +
                "\n\nUse the Admin tab in the Maestro window to edit workflows.",
                "Jaromin Maestro", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void Startup_event()
        {
            EnsureForm();
            _form.ShowOwnedByUccnc();
        }

        public void Showup_event()
        {
            EnsureForm();
            _form.ShowOwnedByUccnc();
        }

        public void Shutdown_event()
        {
            try
            {
                if (_form != null && !_form.IsDisposed)
                    _form.CloseFormSafe();
            }
            catch { }
        }

        public void Loop_event()
        {
            if (loopstop) return;

            loopworking = true;
            try
            {
                if (_form == null || _form.IsDisposed) return;

                if (_firstRun)
                {
                    _firstRun = false;
                }

                _form.UpdateLiveStatus();
            }
            catch { }
            finally
            {
                loopworking = false;
            }
        }

        public object Informplugin_event(object message)
        {
            return null;
        }

        public void Informplugins_event(object message)
        {
        }

        public void Buttonpress_event(int buttonnumber, bool onscreen)
        {
        }

        public void Toolpathclick_event(double x, double y, bool istopview)
        {
        }

        public void Textfieldclick_event(int labelnumber, bool ismainscreen)
        {
        }

        public void Textfieldtexttyped_event(int labelnumber, bool ismainscreen, string text)
        {
        }

        public void Imageviewclick_event(MouseEventArgs e, int labelnumber, bool ismainscreen)
        {
        }

        public void Cyclethreadstart_event()
        {
            if (Engine != null)
                Engine.NotifyCycleStarted();
        }

        public void Cyclethreadfinish_event()
        {
            if (Engine != null)
                Engine.NotifyCycleFinished();
        }

        public void Stoppressed_event()
        {
            if (Engine != null && Engine.IsRunning)
                Engine.RequestAbort();
        }

        public void Resetchanged_event(bool isreset)
        {
            if (isreset && Engine != null && Engine.IsRunning)
                Engine.RequestAbort();
        }

        private void EnsureForm()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new MaestroForm(this);
                Engine = _form.Engine;
            }
        }
    }
}
