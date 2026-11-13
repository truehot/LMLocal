namespace BuildFixture.Lib
{
    // Build benchmark fixture: scenarios replace exact lines of this file before a run,
    // so keep the text of Add and Describe stable or update the scenarios with it.
    public static class Calculator
    {
        public static int Add(int a, int b)
        {
            return a + b;
        }

        public static string Describe(int value)
        {
            return "value=" + value;
        }
    }
}
