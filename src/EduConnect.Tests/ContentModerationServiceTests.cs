using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EduConnect.Tests
{
    // The word list behind Group Finder moderation: read from the
    // ModerationWords table and cached, and filled once at startup from the
    // private Moderation:SeedWords setting. Words here are made up.
    public class ContentModerationServiceTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public void Dispose()
        {
            _cache.Dispose();
            _db.Dispose();
        }

        private ContentModerationService Service() => new(_db.Context, _cache);

        private void AddWord(string word, bool allowed = false)
        {
            _db.Context.ModerationWords.Add(new ModerationWord { Word = word, IsAllowed = allowed });
            _db.Context.SaveChanges();
        }

        private static IConfiguration Config(string? seedWords) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [ModerationWordImporter.SettingKey] = seedWords
                })
                .Build();

        // Keeps what was logged, to check the warning shows no words.
        private sealed class ListLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter) =>
                Entries.Add((logLevel, formatter(state, exception)));
        }

        [Fact]
        public async Task Mask_UsesProhibitedRows_AndAllowedRows()
        {
            AddWord("zorbak");
            AddWord("zorbaak", allowed: true);

            Assert.Equal("****** zorbaak", await Service().MaskAsync("zorbak zorbaak"));
            Assert.True(await Service().ContainsProhibitedAsync("ZORBAK"));
            Assert.False(await Service().ContainsProhibitedAsync(null));
        }

        [Fact]
        public async Task Cache_IsStale_UntilInvalidated()
        {
            var service = Service();
            Assert.Equal("flimbo", await service.MaskAsync("flimbo"));

            AddWord("flimbo");
            Assert.Equal("flimbo", await service.MaskAsync("flimbo"));

            service.InvalidateCache();
            Assert.Equal("******", await service.MaskAsync("flimbo"));
        }

        [Fact]
        public async Task Import_AddsWords_WhenNoProhibitedRows()
        {
            var added = await ModerationWordImporter.ImportAsync(_db.Context,
                Config("zorbak, Flimbo\nflim  bo mo\r\n\n zorbak "), new ListLogger());

            Assert.Equal(3, added);
            var rows = _db.NewContext().ModerationWords.OrderBy(w => w.Word).ToList();
            Assert.Equal(new[] { "flim bo mo", "flimbo", "zorbak" }, rows.Select(w => w.Word));
            Assert.All(rows, w => Assert.False(w.IsAllowed));
            Assert.All(rows, w => Assert.Null(w.Language));
        }

        [Fact]
        public async Task Import_Skips_WhenProhibitedRowsExist()
        {
            AddWord("zabb");

            var added = await ModerationWordImporter.ImportAsync(_db.Context,
                Config("zorbak"), new ListLogger());

            Assert.Equal(0, added);
            Assert.False(_db.NewContext().ModerationWords.Any(w => w.Word == "zorbak"));
        }

        [Fact]
        public async Task Import_IgnoresAllowListRows_WhenCounting()
        {
            AddWord("zorbaak", allowed: true);

            var added = await ModerationWordImporter.ImportAsync(_db.Context,
                Config("zorbak, zorbaak"), new ListLogger());

            Assert.Equal(1, added);
            var rows = _db.NewContext().ModerationWords.ToList();
            Assert.Contains(rows, w => w.Word == "zorbak" && !w.IsAllowed);
            Assert.Single(rows, w => w.Word == "zorbaak");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("   ")]
        public async Task Import_MissingSetting_DoesNothing(string? setting)
        {
            var logger = new ListLogger();

            var added = await ModerationWordImporter.ImportAsync(_db.Context, Config(setting), logger);

            Assert.Equal(0, added);
            Assert.Empty(_db.NewContext().ModerationWords.Where(w => !w.IsAllowed));
            var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
            Assert.DoesNotContain("zorbak", warning.Message);
        }
    }
}
