namespace League.model
{
    public static class ControlExtensions
    {
        public static void InvokeIfRequired(this Control control, Action action)
        {
            if (control.InvokeRequired)
                control.Invoke(new Action(action));
            else
                action();
        }
    }
}
