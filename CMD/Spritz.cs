﻿using CommandLine;
using Proteogenomics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ToolWrapperLayer;
using WorkflowLayer;

namespace CMD
{
    internal class Spritz
    {
        public static void Main(string[] args)
        {
            #region Setup

            if (args.Contains("setup"))
            {
                ManageToolsFlow.Install(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
                return;
            }

            Parsed<Options> result = Parser.Default.ParseArguments<Options>(args) as Parsed<Options>;
            Options options = result.Value;

            #endregion Setup

            #region STAR Fusion Testing

            // TODO make a star fusion 2 protein runner instead of this mess...
            //bool validStarFusionTest = options.AnalysisDirectory != null
            //    && options.BinDirectory != null
            //    && (options.Fastq1 != null || options.st)
            //if (options.Command == "starFusionTest" && !starFusionRequirements.Any(x => x == null))
            //{
            //    Directory.CreateDirectory(Path.Combine(options.AnalysisDirectory, "fusion_out"));
            //    STARFusionWrapper.Install(options.BinDirectory);
            //    STARFusionWrapper.RunStarFusion(options.BinDirectory,
            //        "grch37",
            //        8,
            //        Path.Combine(TestContext.CurrentContext.TestDirectory, "SRR791578_hg19_Chimeric.out.junction"),
            //        new string[0],
            //        Path.Combine(TestContext.CurrentContext.TestDirectory, "fusion_out"));
            //    return;
            //}
            //else if (options.Command == "starFusionTest")
            //{
            //    return;
            //}

            #endregion STAR Fusion Testing

            #region Proteoform Database Engine

            //lncRNA workflow
            if (options.Command.Equals("lncRNADiscovery", StringComparison.InvariantCultureIgnoreCase))
            {
                if (options.Fastq2 != null && options.Fastq1.Count(x => x == ',') != options.Fastq2.Count(x => x == ','))
                    return;

                string[] fastqs1 = options.Fastq1.Split(',');
                List<string[]> fastqsSeparated = options.Fastq2 == null ?
                    fastqs1.Select(x => new string[] { x }).ToList() :
                    fastqs1.Select(x => new string[] { x, options.Fastq2.Split(',')[fastqs1.ToList().IndexOf(x)] }).ToList();

                //LncRNADiscoveryFlow.RunLncRNADiscoveryFromFastq(
                //    options.BinDirectory,
                //    options.AnalysisDirectory,
                //    options.Threads,
                //    fastqsSeparated,
                //    options.StrandSpecific,
                //    options.InferStrandSpecificity,
                //    options.OverwriteStarAlignments,
                //    options.GenomeStarIndexDirectory,
                //    options.GenomeFasta,
                //    options.GeneModelGtfOrGff);

                // LncRNADiscoveryEngine.Test(options.BinDirectory);
                return;
            }

            // finish setup of options

            EnsemblDownloadsWrapper.DownloadReferences(
                options.BinDirectory,
                options.AnalysisDirectory,
                options.Reference,
                out string genomeFastaPath,
                out string gtfGeneModelPath,
                out string gff3GeneModelPath,
                out string proteinFastaPath);

            SnpEffWrapper.DownloadSnpEffDatabase(
                options.BinDirectory,
                options.Reference,
                out string snpEffDatabaseListPath);

            if (options.GenomeStarIndexDirectory == null)
            {
                options.GenomeStarIndexDirectory = Path.Combine(Path.GetDirectoryName(genomeFastaPath), Path.GetFileNameWithoutExtension(genomeFastaPath));
            }

            if (options.GenomeFasta == null)
            {
                options.GenomeFasta = genomeFastaPath;
            }

            if (options.GeneModelGtfOrGff == null)
            {
                options.GeneModelGtfOrGff = gff3GeneModelPath;
            }

            if (options.ReferenceVcf == null)
            {
                GATKWrapper.DownloadEnsemblKnownVariantSites(options.BinDirectory, options.AnalysisDirectory, true, options.Reference, out string ensemblVcfPath);
                options.ReferenceVcf = ensemblVcfPath;
            }

            // run the program

            List<string> proteinDatabases = new List<string>();

            if (options.SraAccession != null && options.SraAccession.StartsWith("SR"))
            {
                SAVProteinDBFlow.GenerateSAVProteinsFromSra(
                    options.BinDirectory,
                    options.AnalysisDirectory,
                    options.Reference,
                    options.Threads,
                    options.SraAccession,
                    options.StrandSpecific,
                    options.InferStrandSpecificity,
                    options.OverwriteStarAlignments,
                    options.GenomeStarIndexDirectory,
                    options.GenomeFasta,
                    proteinFastaPath,
                    options.GeneModelGtfOrGff,
                    options.ReferenceVcf,
                    out proteinDatabases);
            }
            else if (options.Fastq1 != null)
            {
                // Parse comma-separated fastq lists
                if (options.Fastq2 != null && options.Fastq1.Count(x => x == ',') != options.Fastq2.Count(x => x == ','))
                    return;

                string[] fastqs1 = options.Fastq1.Split(',');
                List<string[]> fastqsSeparated = options.Fastq2 == null ?
                    fastqs1.Select(x => new string[] { x }).ToList() :
                    fastqs1.Select(x => new string[] { x, options.Fastq2.Split(',')[fastqs1.ToList().IndexOf(x)] }).ToList();

                SAVProteinDBFlow.GenerateSAVProteinsFromFastqs(
                    options.BinDirectory,
                    options.AnalysisDirectory,
                    options.Reference,
                    options.Threads,
                    fastqsSeparated,
                    options.StrandSpecific,
                    options.InferStrandSpecificity,
                    options.OverwriteStarAlignments,
                    options.GenomeStarIndexDirectory,
                    options.GenomeFasta,
                    proteinFastaPath,
                    options.GeneModelGtfOrGff,
                    options.ReferenceVcf,
                    out proteinDatabases);
            }
            else if (args.Contains("vcf2protein"))
            {
                Genome genome = new Genome(options.GenomeFasta);
                EnsemblDownloadsWrapper.GetImportantProteinAccessions(options.BinDirectory, proteinFastaPath, out var proteinSequences, out HashSet<string> badProteinAccessions, out Dictionary<string, string> selenocysteineContainingAccessions);
                GeneModel geneModel = new GeneModel(genome, options.GeneModelGtfOrGff);
                proteinDatabases.Add(SAVProteinDBFlow.WriteSampleSpecificFasta(options.ReferenceVcf, genome, geneModel, options.Reference, proteinSequences, badProteinAccessions, selenocysteineContainingAccessions, 7, Path.Combine(Path.GetDirectoryName(options.ReferenceVcf), Path.GetFileNameWithoutExtension(options.ReferenceVcf))));
            }
            else
            {
                proteinDatabases = new List<string> { "Error: no fastq or sequence read archive (SRA) was provided." };
            }

            Console.WriteLine("output databases to " + String.Join(", and ", proteinDatabases));
            Console.ReadKey();

            #endregion Proteoform Database Engine
        }
    }
}