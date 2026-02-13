namespace MTFvoiceApp
{
    internal class Program
    {
        private static readonly string DOWNLOADS_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        private static async Task Main(string[] args)
        {
            await RecordTest.MakeARecord(DOWNLOADS_PATH);
        }
    }
}
