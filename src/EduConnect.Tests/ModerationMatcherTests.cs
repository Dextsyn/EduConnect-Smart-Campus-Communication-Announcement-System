using EduConnect.Web.Services;

namespace EduConnect.Tests
{
    // Group Finder word moderation. Every word here is made up: the real
    // list is private (Moderation:SeedWords) because the repo is public.
    public class ModerationMatcherTests
    {
        private static readonly ModerationMatcher Matcher = new(
            new[] { "zorbak", "flimbo", "zabb", "ats", "kinazorbak", "flim bo mo" },
            new[] { "zorbaak" });

        [Theory]
        [InlineData("ZoRbAk mo", "****** mo")]
        [InlineData("ZORBAK", "******")]
        public void CaseInsensitive(string text, string expected) =>
            Assert.Equal(expected, Matcher.Mask(text));

        [Theory]
        [InlineData("zorbaking class")]
        [InlineData("flimbos")]
        public void WholeWordOnly(string text) =>
            Assert.Equal(text, Matcher.Mask(text));

        [Theory]
        [InlineData("z0rb@k", "******")]
        [InlineData("zorb4k", "******")]
        [InlineData("@ts", "***")]
        [InlineData("f1imb0", "******")]
        public void Substitutions(string text, string expected) =>
            Assert.Equal(expected, Matcher.Mask(text));

        [Fact]
        public void StarWildcard() =>
            Assert.Equal("******", Matcher.Mask("z*rbak"));

        [Theory]
        [InlineData("zorbaaaak", "*********")]
        [InlineData("zooorbak", "********")]
        public void StretchedLetters(string text, string expected) =>
            Assert.Equal(expected, Matcher.Mask(text));

        [Fact]
        public void CollapseNeverShortens()
        {
            Assert.Equal("zab", Matcher.Mask("zab"));
            Assert.Equal("******", Matcher.Mask("zabbbb"));
        }

        [Fact]
        public void DigitsOnlyNeverMatch() =>
            Assert.Equal("room 475", Matcher.Mask("room 475"));

        [Theory]
        [InlineData("z.o.r.b.a.k")]
        [InlineData("z o r b a k")]
        [InlineData("z-o-r-b-a-k")]
        [InlineData("z_o_r_b_a_k")]
        public void SeparatorEvasion(string text) =>
            Assert.Equal("***********", Matcher.Mask(text));

        [Fact]
        public void SeparatorRunInsideSentence() =>
            Assert.Equal("hi *********** ok", Matcher.Mask("hi z o r b a k ok"));

        [Fact]
        public void SeparatorRun_OnlyStarsTheWord() =>
            Assert.Equal("a ***********", Matcher.Mask("a z o r b a k"));

        [Theory]
        [InlineData("flim bo mo talaga", "********** talaga")]
        [InlineData("flim  bo   mo", "*************")]
        public void Phrase(string text, string expected) =>
            Assert.Equal(expected, Matcher.Mask(text));

        [Fact]
        public void PhrasePartsAlone_AreClean() =>
            Assert.Equal("flim bo", Matcher.Mask("flim bo"));

        [Fact]
        public void ExplicitAffixForm() =>
            Assert.Equal("**********", Matcher.Mask("kinazorbak"));

        [Fact]
        public void AllowList_Wins()
        {
            Assert.Equal("zorbaak", Matcher.Mask("zorbaak"));
            Assert.Equal("********", Matcher.Mask("zorbaaak"));
        }

        [Fact]
        public void Punctuation_AroundWord_IsKept() =>
            Assert.Equal("(******), ******!", Matcher.Mask("(zorbak), zorbak!"));

        [Fact]
        public void EmojiAndEnye_AreUntouched() =>
            Assert.Equal("******😂 niño", Matcher.Mask("zorbak😂 niño"));

        [Fact]
        public void EmptyLists_ChangeNothing()
        {
            Assert.Equal("zorbak", ModerationMatcher.Empty.Mask("zorbak"));
            Assert.False(ModerationMatcher.Empty.ContainsProhibited("zorbak"));
        }

        [Theory]
        [InlineData("ZoRbAk mo")]
        [InlineData("zorbaking class")]
        [InlineData("z0rb@k")]
        [InlineData("z.o.r.b.a.k")]
        [InlineData("flim bo mo")]
        [InlineData("room 475")]
        [InlineData("zorbaak")]
        [InlineData("zorbak😂")]
        public void ContainsProhibited_MatchesMask(string text) =>
            Assert.Equal(Matcher.Mask(text) != text, Matcher.ContainsProhibited(text));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ContainsProhibited_Blank_IsFalse(string? text) =>
            Assert.False(Matcher.ContainsProhibited(text));

        [Theory]
        [InlineData("  Flim   BO mo ", "flim bo mo")]
        [InlineData("ZORBAK", "zorbak")]
        public void NormalizeEntry_TrimsLowersAndCollapsesSpaces(string word, string expected) =>
            Assert.Equal(expected, ModerationMatcher.NormalizeEntry(word));
    }
}
