﻿/*
 * Original author: Brendan MacLean <brendanx .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2009 University of Washington - Seattle, WA
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using pwiz.Skyline.Model;
using pwiz.Skyline.Model.DocSettings;
using pwiz.Skyline.Model.Results;
using pwiz.Skyline.Model.DocSettings.Extensions;
using pwiz.Skyline.Properties;
using pwiz.Skyline.Util;
using pwiz.SkylineTestUtil;

namespace pwiz.SkylineTestA.Results
{
    /// <summary>
    /// Summary description for FullScanTest
    /// </summary>
    [TestClass]
    public class FullScanTest : AbstractUnitTest
    {
        private const string ZIP_FILE = @"TestA\Results\FullScan.zip";

        /// <summary>
        /// Tests various modes of filtering full-scan results data.
        /// </summary>
        [TestMethod]
        public void FullScanFilterTest()
        {
            List<SrmDocument> docCheckpoints;
            DoFullScanFilterTest(RefinementSettings.ConvertToSmallMoleculesMode.none, out docCheckpoints);
        }

        [TestMethod]
        public void FullScanFilterTestCentroided()
        {
            List<SrmDocument> docCheckpoints;
            if (ExtensionTestContext.CanImportThermoRaw)
                DoFullScanFilterTest(RefinementSettings.ConvertToSmallMoleculesMode.none, out docCheckpoints, true);
        }

        [TestMethod]
        public void FullScanFilterTestAsSmallMolecules()
        {
            List<SrmDocument> docCheckpoints;
            List<SrmDocument> docCheckpointsSM;
            DoFullScanFilterTest(RefinementSettings.ConvertToSmallMoleculesMode.formulas, out docCheckpointsSM);
            DoFullScanFilterTest(RefinementSettings.ConvertToSmallMoleculesMode.none, out docCheckpoints);

            for (var i = 0; i < docCheckpoints.Count; i++)
                CompareDocumentTransitions(docCheckpoints[i], docCheckpointsSM[i]);
        }

        [TestMethod]
        public void FullScanFilterTestAsSmallMoleculesRoundtrip()
        {
            var testFilesDir = new TestFilesDir(TestContext, ZIP_FILE);
            var docPath = testFilesDir.GetTestPath("BSA_Protea_label_free_20100323_meth3_test4.sky");
            var expectedPepCount = 3;
            var expectedTransGroupCount = 4;
            var expectedTransCount = 32;
            InitFullScanDocument(docPath, 3, ref expectedPepCount, ref expectedTransGroupCount,
                ref expectedTransCount, RefinementSettings.ConvertToSmallMoleculesMode.formulas);
        }

        [TestMethod]
        public void FullScanFilterTestAsSmallMoleculeMasses()
        {
            List<SrmDocument> docCheckpoints;
            List<SrmDocument> docCheckpointsSM;
            DoFullScanFilterTest(RefinementSettings.ConvertToSmallMoleculesMode.masses_only, out docCheckpointsSM);
            DoFullScanFilterTest(RefinementSettings.ConvertToSmallMoleculesMode.none, out docCheckpoints);

            for (var i = 0; i < docCheckpointsSM.Count; i++)
                CompareDocumentTransitions(docCheckpoints[i], docCheckpointsSM[i]);
        }

        private void DoFullScanFilterTest(RefinementSettings.ConvertToSmallMoleculesMode asSmallMolecules,
            out List<SrmDocument> docCheckpoints, bool centroided = false)
        {
            docCheckpoints = new List<SrmDocument>();
            TestSmallMolecules = false;  // We test small molecules explicitly

            var testFilesDir = new TestFilesDir(TestContext, ZIP_FILE);
            string docPath = testFilesDir.GetTestPath("BSA_Protea_label_free_20100323_meth3_multi.sky");
            var expectedPepCount = 7;
            var expectedTransGroupCount = 7;
            var expectedTransCount = 49;
            var doc = InitFullScanDocument(docPath, 2, ref expectedPepCount, ref expectedTransGroupCount, ref expectedTransCount, asSmallMolecules);
            if (centroided && ExtensionTestContext.CanImportThermoRaw)
            {
                const double ppm20 = 20.0;
                doc = doc.ChangeSettings(doc.Settings.ChangeTransitionFullScan(fs =>
                    fs.ChangePrecursorResolution(FullScanMassAnalyzerType.centroided, ppm20, 0)));
            }
            var docContainer = new ResultsTestDocumentContainer(doc, docPath);

            // Import the first RAW file (or mzML for international)
            string rawPath = testFilesDir.GetTestPath("ah_20101011y_BSA_MS-MS_only_5-2" +
                ExtensionTestContext.ExtThermoRaw);
            var measuredResults = new MeasuredResults(new[]
                {new ChromatogramSet("Single", new[] {new MsDataFilePath(rawPath)})});

            SrmDocument docResults = docContainer.ChangeMeasuredResults(measuredResults, 3, 3, 21);

            docCheckpoints.Add(docResults);

            // Refilter allowing multiple precursors per spectrum
            SrmDocument docMulti = doc.ChangeSettings(doc.Settings.ChangeTransitionFullScan(
                fs => fs.ChangeAcquisitionMethod(FullScanAcquisitionMethod.DIA, new IsolationScheme("Test", 2))));
            AssertEx.Serializable(docMulti, AssertEx.DocumentCloned);
            // Release data cache file
            Assert.IsTrue(docContainer.SetDocument(docMulti, docResults));
            // And remove it
            FileEx.SafeDelete(Path.ChangeExtension(docPath, ChromatogramCache.EXT));

            docCheckpoints.Add(docContainer.ChangeMeasuredResults(measuredResults, 6, 6, 38));

            // Import full scan Orbi-Velos data
            docPath = testFilesDir.GetTestPath("BSA_Protea_label_free_20100323_meth3_long_acc_template.sky");
            expectedPepCount = 3;
            expectedTransGroupCount = 3;
            expectedTransCount = 21;
            doc = InitFullScanDocument(docPath, 1, ref expectedPepCount, ref expectedTransGroupCount, ref expectedTransCount, asSmallMolecules);
            docCheckpoints.Add(doc);
            Assert.AreEqual(FullScanMassAnalyzerType.orbitrap, doc.Settings.TransitionSettings.FullScan.ProductMassAnalyzer);
            // Make sure saving this type of document works
            AssertEx.Serializable(doc, AssertEx.DocumentCloned);
            Assert.IsTrue(docContainer.SetDocument(doc, docContainer.Document));
            rawPath = testFilesDir.GetTestPath("ah_20101029r_BSA_CID_FT_centroid_3uscan_3" +
                ExtensionTestContext.ExtThermoRaw);
            measuredResults = new MeasuredResults(new[] { new ChromatogramSet("Accurate", new[] { rawPath }) });

            docCheckpoints.Add(docContainer.ChangeMeasuredResults(measuredResults, 3, 3, 21));

            // Import LTQ data with MS1 and MS/MS
            docPath = testFilesDir.GetTestPath("BSA_Protea_label_free_20100323_meth3_test4.sky");
            expectedPepCount = 3;
            expectedTransGroupCount = 4;
            expectedTransCount = 32;
            doc = InitFullScanDocument(docPath, 3, ref expectedPepCount, ref expectedTransGroupCount, ref expectedTransCount, asSmallMolecules);
            Assert.AreEqual(FullScanMassAnalyzerType.none, doc.Settings.TransitionSettings.FullScan.ProductMassAnalyzer);
            Assert.AreEqual(FullScanMassAnalyzerType.none, doc.Settings.TransitionSettings.FullScan.PrecursorMassAnalyzer);
            docCheckpoints.Add(doc);
            var docBoth = doc.ChangeSettings(doc.Settings.ChangeTransitionFullScan(fs =>
                fs.ChangeAcquisitionMethod(FullScanAcquisitionMethod.Targeted, null)
                  .ChangePrecursorResolution(FullScanMassAnalyzerType.qit, TransitionFullScan.DEFAULT_RES_QIT, null)));
            docCheckpoints.Add(docBoth);
            AssertEx.Serializable(docBoth, AssertEx.DocumentCloned);
            Assert.IsTrue(docContainer.SetDocument(docBoth, docContainer.Document));

            string dataPath = testFilesDir.GetTestPath("klc_20100329v_Protea_Peptide_Curve_200fmol_uL_tech1.mzML");
            var listResults = new List<ChromatogramSet>
                                  {
                                      new ChromatogramSet("MS1 and MS/MS", new[] { dataPath }),
                                  };
            measuredResults = new MeasuredResults(listResults.ToArray());

            docCheckpoints.Add(docContainer.ChangeMeasuredResults(measuredResults, expectedPepCount, expectedTransGroupCount, expectedTransCount-6));
            // The mzML was filtered for the m/z range 410 to 910.
            foreach (var nodeTran in docContainer.Document.PeptideTransitions)
            {
                Assert.IsTrue(nodeTran.HasResults);
                Assert.IsNotNull(nodeTran.Results[0]);
                if (410 > nodeTran.Mz || nodeTran.Mz > 910)
                    Assert.IsTrue(nodeTran.Results[0][0].IsEmpty);
                else
                    Assert.IsFalse(nodeTran.Results[0][0].IsEmpty);
            }

            // Import LTQ data with MS1 and MS/MS using multiple files for a single replicate
            listResults.Add(new ChromatogramSet("Multi-file", new[]
                                                                  {
                                                                      testFilesDir.GetTestPath("both_DRV.mzML"),
                                                                      testFilesDir.GetTestPath("both_KVP.mzML"),
                                                                  }));
            measuredResults = new MeasuredResults(listResults.ToArray());
            docCheckpoints.Add(docContainer.ChangeMeasuredResults(measuredResults, expectedPepCount - 1, expectedTransGroupCount-1, expectedTransCount-6));
            int indexResults = listResults.Count - 1;
            foreach (var nodeTran in docContainer.Document.PeptideTransitions)
            {
                Assert.IsTrue(nodeTran.HasResults);
                Assert.AreEqual(listResults.Count, nodeTran.Results.Count);
                var peptide = nodeTran.Transition.Group.Peptide;
                // DRV without FASTA sequence should not have data for non-precursor transitions
                if (!peptide.Sequence.StartsWith("DRV") || !peptide.Begin.HasValue)
                {
                    Assert.IsNotNull(nodeTran.Results[indexResults]);
                    Assert.IsFalse(nodeTran.Results[indexResults][0].IsEmpty);
                }
                else if (nodeTran.Transition.IonType != IonType.precursor)
                    Assert.IsNull(nodeTran.Results[indexResults]);
                else
                {
                    // Random, bogus peaks chosen in both files
                    Assert.IsNotNull(nodeTran.Results[indexResults]);
                    Assert.AreEqual(2, nodeTran.Results[indexResults].Count);
                    Assert.IsFalse(nodeTran.Results[indexResults][0].IsEmpty);
                    Assert.IsFalse(nodeTran.Results[indexResults][1].IsEmpty);
                }
            }

            if (asSmallMolecules == RefinementSettings.ConvertToSmallMoleculesMode.masses_only)
                return; // Can't work with isotope distributions when we don't have ion formulas

            // Verify handling of bad request for vendor centroided data - out-of-range PPM
            docPath = testFilesDir.GetTestPath("Yeast_HI3 Peptides_test.sky");
            expectedPepCount = 2;
            expectedTransGroupCount = 2;
            expectedTransCount = 2;
            doc = InitFullScanDocument(docPath, 2, ref expectedPepCount, ref expectedTransGroupCount, ref expectedTransCount, asSmallMolecules);
            Assert.AreEqual(FullScanMassAnalyzerType.none, doc.Settings.TransitionSettings.FullScan.ProductMassAnalyzer);
            Assert.AreEqual(FullScanMassAnalyzerType.none, doc.Settings.TransitionSettings.FullScan.PrecursorMassAnalyzer);
            var docBad = doc;
            AssertEx.ThrowsException<InvalidDataException>(() => 
              docBad.ChangeSettings(docBad.Settings.ChangeTransitionFullScan(fs =>
                fs.ChangePrecursorIsotopes(FullScanPrecursorIsotopes.Count, 1, IsotopeEnrichments.DEFAULT)
                  .ChangePrecursorResolution(FullScanMassAnalyzerType.centroided, 50 * 1000, 400))),
                  string.Format(Resources.TransitionFullScan_ValidateRes_Mass_accuracy_must_be_between__0__and__1__for_centroided_data_,
                     TransitionFullScan.MIN_CENTROID_PPM,TransitionFullScan.MAX_CENTROID_PPM));

            // Verify relationship between PPM and resolving power
            const double ppm = 20.0;  // Should yield same filter width as resolving power 50,000 in TOF
            var docNoCentroid = doc.ChangeSettings(doc.Settings.ChangeTransitionFullScan(fs =>
                fs.ChangePrecursorIsotopes(FullScanPrecursorIsotopes.Count, 1, IsotopeEnrichments.DEFAULT)
                  .ChangePrecursorResolution(FullScanMassAnalyzerType.centroided, ppm, 0)));
            AssertEx.Serializable(docNoCentroid, AssertEx.DocumentCloned);
            Assert.IsTrue(docContainer.SetDocument(docNoCentroid, docContainer.Document));
            const double mzTest = 400.0;
            var filterWidth = docNoCentroid.Settings.TransitionSettings.FullScan.GetPrecursorFilterWindow(mzTest);
            Assert.AreEqual(mzTest * 2.0 * ppm * 1E-6, filterWidth);

            // Verify handling of bad request for vendor centroided data - ask for centroiding in mzML
            const string fileName = "S_2_LVN.mzML";
            var filePath = testFilesDir.GetTestPath(fileName);
            AssertEx.ThrowsException<AssertFailedException>(() => 
                {
                listResults = new List<ChromatogramSet> { new ChromatogramSet("rep1", new[] {new MsDataFilePath(filePath, null, true)}), };
                docContainer.ChangeMeasuredResults(new MeasuredResults(listResults.ToArray()), 1, 1, 1);
                },
                string.Format(Resources.NoCentroidedDataException_NoCentroidedDataException_No_centroided_data_available_for_file___0_____Adjust_your_Full_Scan_settings_, fileName));

            // Import FT data with only MS1
            docPath = testFilesDir.GetTestPath("Yeast_HI3 Peptides_test.sky");
            expectedPepCount = 2;
            expectedTransGroupCount = 2;
            expectedTransCount = 2;
            doc = InitFullScanDocument(docPath, 2, ref expectedPepCount, ref expectedTransGroupCount, ref expectedTransCount, asSmallMolecules);
            Assert.AreEqual(FullScanMassAnalyzerType.none, doc.Settings.TransitionSettings.FullScan.ProductMassAnalyzer);
            Assert.AreEqual(FullScanMassAnalyzerType.none, doc.Settings.TransitionSettings.FullScan.PrecursorMassAnalyzer);
            var docMs1 = doc.ChangeSettings(doc.Settings.ChangeTransitionFullScan(fs =>
                fs.ChangePrecursorIsotopes(FullScanPrecursorIsotopes.Count, 1, IsotopeEnrichments.DEFAULT)
                  .ChangePrecursorResolution(FullScanMassAnalyzerType.tof, 50 * 1000, null)));
            Assert.AreEqual(filterWidth, docMs1.Settings.TransitionSettings.FullScan.GetPrecursorFilterWindow(mzTest));
            docMs1 = doc.ChangeSettings(doc.Settings.ChangeTransitionFullScan(fs =>
                fs.ChangePrecursorIsotopes(FullScanPrecursorIsotopes.Count, 1, IsotopeEnrichments.DEFAULT)
                  .ChangePrecursorResolution(FullScanMassAnalyzerType.ft_icr, 50 * 1000, mzTest)));
            AssertEx.Serializable(docMs1, AssertEx.DocumentCloned);
            Assert.IsTrue(docContainer.SetDocument(docMs1, docContainer.Document));
            const string rep1 = "rep1";
            listResults = new List<ChromatogramSet>
                                  {
                                      new ChromatogramSet(rep1, new[] {filePath}),
                                  };
            measuredResults = new MeasuredResults(listResults.ToArray());
            docCheckpoints.Add(docContainer.ChangeMeasuredResults(measuredResults, 1, 1, 1));
            // Because of the way the mzML files were filtered, all of the LVN peaks should be present
            // in the first replicate, and all of the NVN peaks should be present in the other.
            foreach (var nodeTranGroup in docContainer.Document.MoleculeTransitionGroups)
            {
                foreach (var docNode in nodeTranGroup.Children)
                {
                    var nodeTran = (TransitionDocNode) docNode;
                    Assert.IsTrue(nodeTran.HasResults);
                    Assert.AreEqual(1, nodeTran.Results.Count);
                    if ((nodeTran.Transition.Group.Peptide.IsCustomIon
                        ? nodeTranGroup.CustomIon.Name
                        : nodeTran.Transition.Group.Peptide.Sequence).StartsWith("LVN"))
                        Assert.IsFalse(nodeTran.Results[0][0].IsEmpty);
                    else
                        Assert.IsTrue(nodeTran.Results[0][0].IsEmpty);
                }
            }
            const string rep2 = "rep2";
            listResults.Add(new ChromatogramSet(rep2, new[] {testFilesDir.GetTestPath("S_2_NVN.mzML")}));
            measuredResults = new MeasuredResults(listResults.ToArray());
            docCheckpoints.Add(docContainer.ChangeMeasuredResults(measuredResults, 1, 1, 1));
            // Because of the way the mzML files were filtered, all of the LVN peaks should be present
            // in the first replicate, and all of the NVN peaks should be present in the other.
            foreach (var nodeTranGroup in docContainer.Document.MoleculeTransitionGroups)
            {
                foreach (var docNode in nodeTranGroup.Children)
                {
                    var nodeTran = (TransitionDocNode) docNode;
                    Assert.IsTrue(nodeTran.HasResults);
                    Assert.AreEqual(2, nodeTran.Results.Count);
                    if ((nodeTran.Transition.Group.Peptide.IsCustomIon
                        ? nodeTranGroup.CustomIon.Name
                        : nodeTran.Transition.Group.Peptide.Sequence).StartsWith("LVN"))
                        Assert.IsTrue(nodeTran.Results[1][0].IsEmpty);
                    else
                        Assert.IsFalse(nodeTran.Results[1][0].IsEmpty);
                }
            }

            // Chromatograms should be present in the cache for a number of isotopes.
            var docMs1Isotopes = docContainer.Document.ChangeSettings(doc.Settings
                .ChangeTransitionFullScan(fs => fs.ChangePrecursorIsotopes(FullScanPrecursorIsotopes.Count,
                                                                           3, IsotopeEnrichments.DEFAULT))
                .ChangeTransitionFilter(filter => filter.ChangeIonTypes(new[] {IonType.precursor})));
            docCheckpoints.Add(docMs1Isotopes);
            AssertEx.IsDocumentState(docMs1Isotopes, null, 2, 2, 2 );   // Need to reset auto-manage for transitions
            var refineAutoSelect = new RefinementSettings { AutoPickChildrenAll = PickLevel.transitions };
            docMs1Isotopes = refineAutoSelect.Refine(docMs1Isotopes);
            AssertEx.IsDocumentState(docMs1Isotopes, null, 2, 2, 6);
            AssertResult.IsDocumentResultsState(docMs1Isotopes, rep1, 1, 1, 0, 3, 0);
            AssertResult.IsDocumentResultsState(docMs1Isotopes, rep2, 1, 1, 0, 3, 0);
            docCheckpoints.Add(docMs1Isotopes);

            // Add M-1 transitions, and verify that they have chromatogram data also, but
            // empty peaks in all cases
            var docMs1All = docMs1Isotopes.ChangeSettings(docMs1Isotopes.Settings
                .ChangeTransitionFullScan(fs => fs.ChangePrecursorIsotopes(FullScanPrecursorIsotopes.Percent,
                                                                           0, IsotopeEnrichments.DEFAULT))
                .ChangeTransitionIntegration(i => i.ChangeIntegrateAll(false)));    // For compatibility with v2.5 and earlier
            docCheckpoints.Add(docMs1All);
            AssertEx.IsDocumentState(docMs1All, null, 2, 2, 10);
            AssertResult.IsDocumentResultsState(docMs1All, rep1, 1, 1, 0, 4, 0);
            AssertResult.IsDocumentResultsState(docMs1All, rep2, 1, 1, 0, 4, 0);
            var ms1AllTranstions = docMs1All.MoleculeTransitions.ToArray();
            var tranM1 = ms1AllTranstions[0];
            Assert.AreEqual(-1, tranM1.Transition.MassIndex);
            Assert.IsTrue(tranM1.Results[0] != null && tranM1.Results[1] != null);
            Assert.IsTrue(tranM1.Results[0][0].IsEmpty && tranM1.Results[1][0].IsEmpty);
            tranM1 = ms1AllTranstions[5];
            Assert.AreEqual(-1, tranM1.Transition.MassIndex);
            Assert.IsTrue(tranM1.Results[0] != null && tranM1.Results[1] != null);
            Assert.IsTrue(tranM1.Results[0][0].IsEmpty && tranM1.Results[1][0].IsEmpty);
        }

        private static SrmDocument InitFullScanDocument(string docPath, int prot, ref int pep, ref int prec, ref int tran, RefinementSettings.ConvertToSmallMoleculesMode smallMoleculeTestMode)
        {
            SrmDocument doc = ResultsUtil.DeserializeDocument(docPath);
            int? expectedRevisionNumber = 0;
            if (smallMoleculeTestMode != RefinementSettings.ConvertToSmallMoleculesMode.none)
            {
                var refine = new RefinementSettings();
                var oldDoc = doc;
                AssertEx.Serializable(oldDoc);
                doc = refine.ConvertToSmallMolecules(doc, smallMoleculeTestMode);
                AssertEx.ConvertedSmallMoleculeDocumentIsSimilar(oldDoc, doc);
                AssertEx.Serializable(doc);
                expectedRevisionNumber = null;

            }
            AssertEx.IsDocumentState(doc, expectedRevisionNumber, prot, pep, prec, tran);
            return doc;
        }

        /// <summary>
        /// Tests full-scan settings changes and their impact on the document.
        /// </summary>
        [TestMethod]
        public void FullScanSettingsTest()
        {
            List<SrmDocument> docCheckpoints;
            DoFullScanSettingsTest(RefinementSettings.ConvertToSmallMoleculesMode.none, out docCheckpoints);
        }

        [TestMethod]
        public void FullScanSettingsTestAsSmallMolecules()
        {
            List<SrmDocument> docCheckpoints;
            List<SrmDocument> docCheckpointsSM;

            DoFullScanSettingsTest(RefinementSettings.ConvertToSmallMoleculesMode.formulas, out docCheckpointsSM);
            DoFullScanSettingsTest(RefinementSettings.ConvertToSmallMoleculesMode.none, out docCheckpoints);

            for (var i = 0; i < docCheckpoints.Count; i++)
                CompareDocumentTransitions(docCheckpoints[i], docCheckpointsSM[i]);
        }

        public void DoFullScanSettingsTest(RefinementSettings.ConvertToSmallMoleculesMode asSmallMolecules, 
            out List<SrmDocument> docCheckPoints)
        {
            TestSmallMolecules = false; // Don't need that magic extra node, we have an explicit test

            docCheckPoints = new List<SrmDocument>();
            var doc = ResultsUtil.DeserializeDocument("MultiLabel.sky", GetType());
            var refine = new RefinementSettings();
            doc = refine.ConvertToSmallMolecules(doc, asSmallMolecules);
            Assert.IsFalse(doc.MoleculeTransitionGroups.Any(nodeGroup => nodeGroup.IsotopeDist != null));
            AssertEx.Serializable(doc, AssertEx.Cloned);

            double c13Delta = BioMassCalc.MONOISOTOPIC.GetMass(BioMassCalc.C13) -
                              BioMassCalc.MONOISOTOPIC.GetMass(BioMassCalc.C);
            double n15Delta = BioMassCalc.MONOISOTOPIC.GetMass(BioMassCalc.N15) -
                              BioMassCalc.MONOISOTOPIC.GetMass(BioMassCalc.N);

            // Verify isotope distributions calculated when MS1 filtering enabled
            var enrichments = IsotopeEnrichments.DEFAULT;
            var docIsotopes = doc.ChangeSettings(doc.Settings.ChangeTransitionFullScan(fs =>
                fs.ChangePrecursorIsotopes(FullScanPrecursorIsotopes.Count, 3, enrichments)));
            docCheckPoints.Add(docIsotopes);
            Assert.AreEqual(FullScanMassAnalyzerType.tof,
                docIsotopes.Settings.TransitionSettings.FullScan.PrecursorMassAnalyzer);
            Assert.IsFalse(docIsotopes.MoleculeTransitionGroups.Any(nodeGroup => nodeGroup.IsotopeDist == null));
            foreach (var nodeGroup in docIsotopes.MoleculeTransitionGroups)
            {
                Assert.AreEqual(3, nodeGroup.Children.Count);
                var isotopePeaks = nodeGroup.IsotopeDist;
                Assert.IsNotNull(isotopePeaks);
                Assert.IsTrue(nodeGroup.HasIsotopeDist);
                // The peaks should always includ at least M-1
                Assert.IsTrue(isotopePeaks.MassIndexToPeakIndex(0) > 0);
                // Within 2.5% of 100% of the entire isotope distribution
                Assert.AreEqual(1.0, isotopePeaks.ExpectedProportions.Sum(), 0.025);

                // Precursor mass and m/z values are expected to match exactly (well, within XML roundtrip accuracy anyway)
                Assert.AreEqual(nodeGroup.PrecursorMz, nodeGroup.IsotopeDist.GetMZI(0), SequenceMassCalc.MassTolerance);
                Assert.AreEqual(nodeGroup.PrecursorMz, nodeGroup.TransitionGroup.IsCustomIon ?
                                BioMassCalc.CalculateIonMz(nodeGroup.IsotopeDist.GetMassI(0),
                                                       nodeGroup.TransitionGroup.PrecursorCharge) :
                                SequenceMassCalc.GetMZ(nodeGroup.IsotopeDist.GetMassI(0),
                                                       nodeGroup.TransitionGroup.PrecursorCharge), SequenceMassCalc.MassTolerance);

                // Check isotope distribution masses
                for (int i = 1; i < isotopePeaks.CountPeaks; i++)
                {
                    int massIndex = isotopePeaks.PeakIndexToMassIndex(i);
                    Assert.IsTrue(isotopePeaks.GetMZI(massIndex - 1) < isotopePeaks.GetMZI(massIndex));
                    double massDelta = GetMassDelta(isotopePeaks, massIndex);
                    if (nodeGroup.TransitionGroup.LabelType.IsLight)
                    {
                        // All positive should be close to 13C - C, and 0 should be the same as the next delta
                        double expectedDelta = (massIndex > 0 ? c13Delta : GetMassDelta(isotopePeaks, massIndex + 1));
                        Assert.AreEqual(expectedDelta, massDelta, 0.001);
                    }
                    else if (nodeGroup.TransitionGroup.LabelType.Name.Contains("15N"))
                    {
                        // All positive should be close to 13C, and all negative 15N
                        double expectedDelta = (massIndex > 0 ? c13Delta : n15Delta);
                        Assert.AreEqual(expectedDelta, massDelta, 0.0015);
                    }
                    else if (massIndex == 0)
                    {
                        double expectedDelta = (isotopePeaks.GetProportionI(massIndex - 1) == 0
                                                    ? GetMassDelta(isotopePeaks, massIndex + 1)
                                                    : 1.0017);
                        Assert.AreEqual(expectedDelta, massDelta, 0.001);
                    }
                    else
                    {
                        Assert.AreEqual(c13Delta, massDelta, 0.001);
                    }
                }
            }
            AssertEx.Serializable(docIsotopes, AssertEx.Cloned);

            // Narrow the resolution, and verify that predicted proportion of the isotope
            // distribution captured is reduced for all precursors
            var docIsotopesFt = docIsotopes.ChangeSettings(docIsotopes.Settings.ChangeTransitionFullScan(fs =>
                fs.ChangePrecursorResolution(FullScanMassAnalyzerType.ft_icr, 500 * 1000, 400)));
            docCheckPoints.Add(docIsotopesFt);
            var tranGroupsOld = docIsotopes.MoleculeTransitionGroups.ToArray();
            var tranGroupsNew = docIsotopesFt.MoleculeTransitionGroups.ToArray();
            Assert.AreEqual(tranGroupsOld.Length, tranGroupsNew.Length);
            for (int i = 0; i < tranGroupsOld.Length; i++)
            {
                Assert.AreNotSame(tranGroupsOld[i], tranGroupsNew[i]);
                Assert.AreNotSame(tranGroupsOld[i].IsotopeDist, tranGroupsNew[i].IsotopeDist);
                Assert.IsTrue(tranGroupsOld[i].IsotopeDist.ExpectedProportions.Sum() >
                    tranGroupsNew[i].IsotopeDist.ExpectedProportions.Sum());
            }

            // Use Min % of base peak and verify variation in transitions used
            const float minPercent1 = 10;
            var docIsotopesP1 = docIsotopes.ChangeSettings(docIsotopes.Settings.ChangeTransitionFullScan(fs =>
                fs.ChangePrecursorIsotopes(FullScanPrecursorIsotopes.Percent, minPercent1, enrichments)));
            docCheckPoints.Add(docIsotopesP1);
            tranGroupsNew = docIsotopesP1.MoleculeTransitionGroups.ToArray();
            int maxTran = 0;
            for (int i = 0; i < tranGroupsOld.Length; i++)
            {
                // Isotope distributions should not have changed
                var isotopePeaks = tranGroupsNew[i].IsotopeDist;
                Assert.AreSame(tranGroupsOld[i].IsotopeDist, isotopePeaks);
                // Expected transitions should be present
                maxTran = Math.Max(maxTran, tranGroupsNew[i].Children.Count);
                foreach (TransitionDocNode nodeTran in tranGroupsNew[i].Children)
                {
                    int massIndex = nodeTran.Transition.MassIndex;
                    Assert.IsTrue(minPercent1 <= isotopePeaks.GetProportionI(massIndex)*100.0/isotopePeaks.BaseMassPercent);
                }
            }
            Assert.AreEqual(5, maxTran);
            AssertEx.Serializable(docIsotopesP1, AssertEx.Cloned);  // Express any failure in terms of XML diffs

            // Use 10%, and check that 15N modifications all have M-1
            const float minPercent2 = 5;
            var docIsotopesP2 = docIsotopesP1.ChangeSettings(docIsotopesP1.Settings.ChangeTransitionFullScan(fs =>
                fs.ChangePrecursorIsotopes(FullScanPrecursorIsotopes.Percent, minPercent2, enrichments)));
            docCheckPoints.Add(docIsotopesP2);

            foreach (var nodeGroup in docIsotopesP2.MoleculeTransitionGroups)
            {
                var firstChild = (TransitionDocNode) nodeGroup.Children[0];
                if (nodeGroup.TransitionGroup.LabelType.Name.EndsWith("15N"))
                    Assert.AreEqual(-1, firstChild.Transition.MassIndex);
                else
                    Assert.AreNotEqual(-1, firstChild.Transition.MassIndex);
            }
            AssertEx.Serializable(docIsotopesP2, AssertEx.Cloned);

            // Use lower enrichment of 13C, and verify that this add M-1 for 13C labeled precursors
            var enrichmentsLow13C = enrichments.ChangeEnrichment(new IsotopeEnrichmentItem(BioMassCalc.C13, 0.9));
            var docIsotopesLow13C = docIsotopesP1.ChangeSettings(docIsotopesP1.Settings.ChangeTransitionFullScan(fs =>
                fs.ChangePrecursorIsotopes(FullScanPrecursorIsotopes.Percent, minPercent2, enrichmentsLow13C)));
            tranGroupsNew = docIsotopesLow13C.MoleculeTransitionGroups.ToArray();
            for (int i = 0; i < tranGroupsOld.Length; i++)
            {
                var nodeGroup = tranGroupsNew[i];
                if (!Equals(nodeGroup.TransitionGroup.LabelType.Name, "heavy"))
                    Assert.AreSame(tranGroupsOld[i].IsotopeDist, nodeGroup.IsotopeDist);
                else
                {
                    var firstChild = (TransitionDocNode)nodeGroup.Children[0];
                    Assert.IsTrue(firstChild.Transition.MassIndex < 0);
                }
            }
            AssertEx.Serializable(docIsotopesLow13C, AssertEx.Cloned); // Express any failure as XML diffs

            // Use 0%, and check that everything has M-1 and lower
            var enrichmentsLow = enrichmentsLow13C.ChangeEnrichment(new IsotopeEnrichmentItem(BioMassCalc.N15, 0.97));
            var docIsotopesLowP0 = docIsotopesP1.ChangeSettings(docIsotopesP1.Settings.ChangeTransitionFullScan(fs =>
                fs.ChangePrecursorIsotopes(FullScanPrecursorIsotopes.Percent, 0, enrichmentsLow)));
            docCheckPoints.Add(docIsotopesLowP0);
            foreach (var nodeGroup in docIsotopesLowP0.MoleculeTransitionGroups) 
            {
                Assert.AreEqual(nodeGroup.IsotopeDist.CountPeaks, nodeGroup.Children.Count);
                var firstChild = (TransitionDocNode)nodeGroup.Children[0];
                if (nodeGroup.TransitionGroup.LabelType.IsLight)
                    Assert.AreEqual(-1, firstChild.Transition.MassIndex);
                else
                    Assert.IsTrue(-1 > firstChild.Transition.MassIndex);
            }
            AssertEx.Serializable(docIsotopesLowP0, AssertEx.Cloned);

            // Test a document with variable and heavy modifications, which caused problems for
            // the original implementation
            var docVariable = ResultsUtil.DeserializeDocument("HeavyVariable.sky", GetType());
            Assert.IsFalse(docVariable.MoleculeTransitionGroups.Any(nodeGroup => nodeGroup.IsotopeDist == null));

            foreach (var nodeGroup in docVariable.MoleculeTransitionGroups)
            {
                var isotopePeaks = nodeGroup.IsotopeDist;
                Assert.IsNotNull(isotopePeaks);
                // The peaks should always includ at least M-1
                Assert.IsTrue(isotopePeaks.MassIndexToPeakIndex(0) > 0);
                // Precursor mass and m/z values are expected to match exactly (well, within XML roundtrip tolerance anyway)
                Assert.AreEqual(nodeGroup.PrecursorMz, nodeGroup.IsotopeDist.GetMZI(0), SequenceMassCalc.MassTolerance);

                // Check isotope distribution masses
                for (int i = 1; i < isotopePeaks.CountPeaks; i++)
                {
                    int massIndex = isotopePeaks.PeakIndexToMassIndex(i);
                    Assert.IsTrue(isotopePeaks.GetMZI(massIndex - 1) < isotopePeaks.GetMZI(massIndex));
                    double massDelta = GetMassDelta(isotopePeaks, massIndex);
                    bool containsSulfur = nodeGroup.TransitionGroup.Peptide.IsCustomIon
                        ? (nodeGroup.CustomIon.Formula.IndexOfAny("S".ToCharArray()) != -1)
                        : (nodeGroup.TransitionGroup.Peptide.Sequence.IndexOfAny("CM".ToCharArray()) != -1);
                    if (massIndex == 0)
                    {
                        double expectedDelta = (isotopePeaks.GetProportionI(massIndex - 1) == 0
                                                    ? GetMassDelta(isotopePeaks, massIndex + 1)
                                                    : 1.0017);
                        Assert.AreEqual(expectedDelta, massDelta, 0.001);
                    }
                    else if (!containsSulfur || massIndex == 1)
                    {
                        Assert.AreEqual(c13Delta, massDelta, 0.001);
                    }
                    else
                    {
                        Assert.AreEqual(1.00075, massDelta, 0.001);
                    }
                }
            }
            docCheckPoints.Add(docVariable);
        }

        private static double GetMassDelta(IsotopeDistInfo isotopeDist, int massIndex)
        {
            return isotopeDist.GetMassI(massIndex) - isotopeDist.GetMassI(massIndex - 1);
        }

        private void CompareDocumentTransitions(SrmDocument docA, SrmDocument docB)
        {
            // Note that we can't just run the lists in parallel - one of these is likely a small molecule conversion of the other,
            // and will have its transitions sorted differently
            Assert.AreEqual(docA.MoleculeTransitionGroupCount, docB.MoleculeTransitionGroupCount);
            var docBTransitionGroups = docB.MoleculeTransitionGroups.GetEnumerator();
            foreach (var transGroupDocA in docA.MoleculeTransitionGroups)
            {
                Assert.IsTrue(docBTransitionGroups.MoveNext());
                var transGroupDocB = docBTransitionGroups.Current;
                Assert.AreEqual(transGroupDocA.TransitionCount, transGroupDocB.TransitionCount);
                var docBTransitions = transGroupDocB.Transitions.ToArray();
                var docBTransitionsMatched = new List<TransitionDocNode>();
                foreach (var transDocA in transGroupDocA.Transitions)
                {
                    var a = transDocA;
                    var transDocB = docBTransitions.FirstOrDefault(b => 
                        a.Transition.IsPrecursor() == b.Transition.IsPrecursor() &&
                        Math.Abs(a.Mz - b.Mz) <= 1.0E-5 &&
                        ((a.Results == null)
                            ? (b.Results == null)
                            : (a.Results.Count == b.Results.Count)));
                    Assert.IsNotNull(transDocB, "failed to find matching transition");
                    Assert.IsFalse(docBTransitionsMatched.Contains(transDocB), "transition matched twice");
                    docBTransitionsMatched.Add(transDocB);
                    var docBChromInfos = transDocB.ChromInfos.GetEnumerator();
                    foreach (var infoDocA in transDocA.ChromInfos)
                    {
                        Assert.IsTrue(docBChromInfos.MoveNext());
                        var infoDocB = docBChromInfos.Current;
                        Assert.IsTrue(infoDocA.Equals(infoDocB));
                    }
                }
            }
        }
    }
}