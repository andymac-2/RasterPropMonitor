namespace JSI
{
    public class PatchSelector
    {
        private int buttonNext = 7;
        private int buttonPrev = 8;
        private RasterPropMonitor rpm;

        internal PatchSelector(RasterPropMonitor rpm, ConfigNode node)
        {
            this.rpm = rpm;
            int intValue = 0;

            if (node.TryGetValue("buttonNext", ref intValue))
            {
                buttonNext = intValue;
            }

            if (node.TryGetValue("buttonPrev", ref intValue))
            {
                buttonPrev = intValue;
            }
        }

        internal void HandleButtonPress(int buttonID)
        {
            if (buttonID == buttonNext)
            {
                rpm.SelectNextPatch();
            }

            if (buttonID == buttonPrev)
            {
                rpm.SelectPreviousPatch();
            }
        }
    }
}
