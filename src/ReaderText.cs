namespace FloatLeaf
{
    internal static class ReaderText
    {
        internal static volatile bool English;

        internal static string Get(string chinese, string english)
        {
            return English ? english : chinese;
        }
    }
}
