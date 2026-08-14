using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4PKGTool.Utilities;

namespace PS4PKGTool.Tests
{
    [TestClass]
    public class PkgFilterStateTests
    {
        [TestMethod]
        public void EmptyState_ProducesNoFilter()
        {
            Assert.AreEqual("", PkgFilter.BuildExpression(new PkgFilterState()));
        }

        [TestMethod]
        public void Categories_OrWithinAndAcross()
        {
            var state = new PkgFilterState();
            state.Categories.Add("Game");
            state.Categories.Add("Patch");

            string expr = PkgFilter.BuildExpression(state);

            StringAssert.Contains(expr, "[Category] LIKE '%Game%'");
            StringAssert.Contains(expr, "[Category] LIKE '%Patch%'");
            Assert.IsTrue(expr.Contains(" OR "), "categories OR within the aspect");
            Assert.IsFalse(expr.Contains(" AND "), "single aspect - no AND");
        }

        [TestMethod]
        public void Regions_FilterOnTheHiddenNameColumn()
        {
            var state = new PkgFilterState();
            state.Regions.Add("EU");
            state.Regions.Add("US");

            string expr = PkgFilter.BuildExpression(state);

            StringAssert.Contains(expr, "[Region Name] = 'EU'");
            StringAssert.Contains(expr, "[Region Name] = 'US'");
        }

        [TestMethod]
        public void SystemVersion_ThresholdUsesNumericColumn()
        {
            var state = new PkgFilterState { MinSystemVersion = 5.05 };

            string expr = PkgFilter.BuildExpression(state);

            Assert.AreEqual("[System Version (Num)] >= 5.05", expr);
        }

        [TestMethod]
        public void PkgTypes_ExactMatch()
        {
            var state = new PkgFilterState();
            state.PkgTypes.Add("Official");

            Assert.AreEqual("[PKG Type] = 'Official'", PkgFilter.BuildExpression(state));
        }

        [TestMethod]
        public void CompatStatuses_UnknownUsesNullOrEmptyClause()
        {
            var state = new PkgFilterState();
            state.CompatStatuses.Add("Playable");
            state.CompatStatuses.Add(PkgFilter.CompatUnknown);

            string expr = PkgFilter.BuildExpression(state);

            StringAssert.Contains(expr, "[ShadPS4] = 'Playable'");
            StringAssert.Contains(expr, "([ShadPS4] IS NULL OR [ShadPS4] = '')");
        }

        [TestMethod]
        public void AllAspects_ComposeWithAnd()
        {
            var state = new PkgFilterState
            {
                MinSystemVersion = 5.05,
                SearchText = "blood",
            };
            state.Categories.Add("Game");
            state.Regions.Add("EU");
            state.PkgTypes.Add("Official");
            state.CompatStatuses.Add("Playable");

            string expr = PkgFilter.BuildExpression(state);

            Assert.AreEqual(6, expr.Split(" AND ").Length,
                "each aspect composes with AND");
            StringAssert.Contains(expr, "LIKE '%blood%'");
        }

        [TestMethod]
        public void Search_EscapesQuotes()
        {
            var state = new PkgFilterState { SearchText = "it's" };
            Assert.IsTrue(PkgFilter.BuildExpression(state).Contains("it''s"));
        }

        [TestMethod]
        public void ParseSystemVersionNum_HandlesNaAndJunk()
        {
            Assert.AreEqual(5.05, PkgColumns.ParseSystemVersionNum("5.05"));
            Assert.AreEqual(9.0, PkgColumns.ParseSystemVersionNum("9.00"));
            Assert.AreEqual(0, PkgColumns.ParseSystemVersionNum("NA"));
            Assert.AreEqual(0, PkgColumns.ParseSystemVersionNum(""));
            Assert.AreEqual(10.0, PkgColumns.ParseSystemVersionNum("10.00"), "10.00 sorts above 9.x numerically");
        }

        [TestMethod]
        public void LegacyOverload_StillBuildsTheOldExpression()
        {
            string expr = PkgFilter.BuildExpression("Game", "Playable", "blood");
            Assert.IsTrue(expr.Contains("[Category] LIKE '%Game%'"));
            Assert.IsTrue(expr.Contains("[ShadPS4] = 'Playable'"));
            Assert.IsTrue(expr.Contains("'%blood%'"));
            Assert.AreEqual(3, expr.Split(" AND ").Length);
        }
    }
}
