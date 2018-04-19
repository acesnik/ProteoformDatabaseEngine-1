﻿using Bio.VCF;
using NUnit.Framework;
using Proteogenomics;
using Proteomics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ToolWrapperLayer;
using UsefulProteomicsDatabases;

namespace Test
{
    [TestFixture]
    public class VariantTests
    {
        [Test]
        public void AmendTranscripts()
        {
            Genome genome = new Genome(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_sample.fa"));
            VCFParser vcf99999 = new VCFParser(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_99999.vcf")); // added a snpeff variant to this one
            VCFParser vcf100000 = new VCFParser(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_100000.vcf"));
            VCFParser vcf100001 = new VCFParser(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_100001.vcf"));
            VCFParser vcf400000 = new VCFParser(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_400000.vcf"));
            VCFParser vcf400001 = new VCFParser(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_400001.vcf"));
            List<Variant> variants99999 = vcf99999.Select(x => new Variant(null, x, genome)).ToList();
            List<Variant> variants100000 = vcf100000.Select(x => new Variant(null, x, genome)).ToList();
            List<Variant> variants100001 = vcf100001.Select(x => new Variant(null, x, genome)).ToList();
            List<Variant> variants400000 = vcf400000.Select(x => new Variant(null, x, genome)).ToList();
            List<Variant> variants400001 = vcf400001.Select(x => new Variant(null, x, genome)).ToList();

            GeneModel geneModel;
            geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_fake_transcript.gtf"));
            List<Transcript> transcripts1 = geneModel.ApplyVariants(variants99999);
            Assert.AreEqual(0, transcripts1[0].Exons[0].Variants.Count);

            geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_fake_transcript.gtf"));
            List<Transcript> transcripts2 = geneModel.ApplyVariants(variants100000);
            Assert.AreEqual(1, transcripts2[0].Exons[0].Variants.Count);
            geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_fake_transcript_little_exon.gtf"));
            List<Transcript> transcripts3 = geneModel.ApplyVariants(variants100000);
            Assert.AreEqual(0, transcripts3[0].Exons[0].Variants.Count);

            geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_fake_transcript.gtf"));
            List<Transcript> transcripts4 = geneModel.ApplyVariants(variants100001);
            Assert.AreEqual(1, transcripts4[0].Exons[0].Variants.Count);
            geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_fake_transcript_little_exon.gtf"));
            List<Transcript> transcripts5 = geneModel.ApplyVariants(variants100001);
            Assert.AreEqual(0, transcripts5[0].Exons[0].Variants.Count);

            geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_fake_transcript.gtf"));
            List<Transcript> transcripts6 = geneModel.ApplyVariants(variants400000);
            Assert.AreEqual(1, transcripts6[0].Exons[0].Variants.Count);
            geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_fake_transcript_little_exon.gtf"));
            List<Transcript> transcripts7 = geneModel.ApplyVariants(variants400000);
            Assert.AreEqual(0, transcripts7[0].Exons[0].Variants.Count);

            geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_fake_transcript.gtf"));
            var transcripts8 = geneModel.ApplyVariants(variants400001);
            Assert.AreEqual(0, transcripts8[0].Exons[0].Variants.Count);
        }

        [Test]
        public void OneTranscriptOneHomozygous()
        {
            Genome genome = new Genome(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_sample.fa"));
            VCFParser vcf = new VCFParser(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_homozygous_missense.vcf"));
            List<Variant> variants = vcf.Select(x => new Variant(null, x, genome)).ToList();
            Assert.AreEqual(1, variants.Count);

            GeneModel geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_transcript.gtf"));
            List<Protein> proteins_wo_variant = geneModel.Translate(true).ToList();
            List<Transcript> transcripts = geneModel.ApplyVariants(variants);
            List<Protein> proteins = transcripts.Select(t => t.Protein()).ToList();
            Assert.AreEqual(1, geneModel.Genes.Count);
            Assert.AreEqual(1, proteins.Count);
            Assert.AreEqual(1, proteins_wo_variant.Count);
            Assert.AreEqual(2, new HashSet<string> { proteins[0].BaseSequence, proteins_wo_variant[0].BaseSequence }.Count);
            Assert.IsTrue(proteins[0].FullName != null);
            Assert.IsTrue(proteins[0].FullName.Contains(FunctionalClass.MISSENSE.ToString())); // sav
            Assert.IsTrue(proteins[0].FullName.Contains(GenotypeType.HOMOZYGOUS_ALT.ToString())); // sav
            Assert.IsTrue(proteins[0].FullName.Contains("1:69640"));

            string proteinFasta = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_homozygous_missense.fasta");
            ProteinDbWriter.WriteFastaDatabase(proteins, proteinFasta, " ");
            string[] proteinFastaLines = File.ReadLines(proteinFasta).ToArray();
            Assert.IsTrue(proteinFastaLines[0].Contains(FunctionalClass.MISSENSE.ToString())); //sav
            Assert.IsTrue(proteinFastaLines[0].Contains("1:69640"));
        }

        [Test]
        public void OneTranscriptOneHeterozygous()
        {
            Genome genome = new Genome(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_sample.fa"));
            VCFParser vcf = new VCFParser(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_heterozygous_missense.vcf"));
            List<Variant> variants = vcf.Select(x => new Variant(null, x, genome)).ToList();
            Assert.AreEqual(1, variants.Count);

            GeneModel geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_transcript.gtf"));
            List<Protein> proteins_wo_variant = geneModel.Translate(true).ToList();
            List<Transcript> transcripts = geneModel.ApplyVariants(variants);
            List<Protein> proteins = transcripts.Select(t => t.Protein()).ToList();
            Assert.AreEqual(1, geneModel.Genes.Count);
            Assert.AreEqual(2, proteins.Count);
            Assert.AreEqual(1, proteins_wo_variant.Count);
            Assert.AreEqual(2, new HashSet<string> { proteins[0].BaseSequence, proteins[1].BaseSequence, proteins_wo_variant[0].BaseSequence }.Count);
            Assert.IsTrue(proteins.All(p => p.FullName != null));
            Assert.IsTrue(proteins.Any(p => p.FullName.Contains(FunctionalClass.MISSENSE.ToString()))); // sav
            Assert.IsTrue(proteins.Any(p => p.FullName.Contains(GenotypeType.HETEROZYGOUS.ToString()))); // sav
            Assert.IsTrue(proteins.Any(p => p.FullName.Contains("1:69640")));

            string proteinFasta = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_heterozygous_missense.fasta");
            ProteinDbWriter.WriteFastaDatabase(proteins, proteinFasta, " ");
            string[] proteinFastaLines = File.ReadLines(proteinFasta).ToArray();
            Assert.IsTrue(proteinFastaLines.Any(x => x.Contains(FunctionalClass.MISSENSE.ToString()))); // sav
            Assert.IsTrue(proteinFastaLines.Any(x => x.Contains("1:69640")));
        }

        [Test]
        public void OneTranscriptOneHeterozygousSynonymous()
        {
            Genome genome = new Genome(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_sample.fa"));
            VCFParser vcf = new VCFParser(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_heterozygous_synonymous.vcf"));
            List<Variant> variants = vcf.Select(x => new Variant(null, x, genome)).ToList();
            Assert.AreEqual(1, variants.Count);

            GeneModel geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_transcript.gtf"));
            List<Protein> proteins_wo_variant = geneModel.Translate(true).ToList();
            List<Transcript> transcripts = geneModel.ApplyVariants(variants);
            List<Protein> proteins = transcripts.Select(t => t.Protein()).ToList();
            Assert.AreEqual(1, geneModel.Genes.Count);
            Assert.AreEqual(1, proteins.Count);
            Assert.AreEqual(1, proteins_wo_variant.Count);
            Assert.AreEqual(1, new HashSet<string> { proteins[0].BaseSequence, proteins_wo_variant[0].BaseSequence }.Count);
            Assert.IsTrue(proteins.Any(p => p.FullName.Contains(FunctionalClass.SILENT.ToString()))); // synonymous
            Assert.IsTrue(proteins.Any(p => p.FullName.Contains(GenotypeType.HETEROZYGOUS.ToString()))); // synonymous
            Assert.IsTrue(proteins.Any(p => p.FullName.Contains("1:69666")));

            string proteinFasta = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_heterozygous_synonymous.fasta");
            ProteinDbWriter.WriteFastaDatabase(proteins, proteinFasta, " ");
            string[] proteinFastaLines = File.ReadLines(proteinFasta).ToArray();
            Assert.IsTrue(proteinFastaLines[0].Contains(FunctionalClass.SILENT.ToString())); // synonymous
            Assert.IsTrue(proteinFastaLines[0].Contains("1:69666"));
        }

        [Test]
        public void OneTranscriptOneHomozygousSynonymous()
        {
            Genome genome = new Genome(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_sample.fa"));
            VCFParser vcf = new VCFParser(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_homozygous_synonymous.vcf"));
            List<Variant> variants = vcf.Select(x => new Variant(null, x, genome)).ToList();
            Assert.AreEqual(1, variants.Count);

            GeneModel geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_transcript.gtf"));
            List<Protein> proteins_wo_variant = geneModel.Translate(true).ToList();
            List<Transcript> transcripts = geneModel.ApplyVariants(variants);
            List<Protein> proteins = transcripts.Select(t => t.Protein()).ToList();
            Assert.AreEqual(1, geneModel.Genes.Count);
            Assert.AreEqual(1, proteins.Count);
            Assert.AreEqual(1, proteins_wo_variant.Count);
            Assert.AreEqual(1, new HashSet<string> { proteins[0].BaseSequence, proteins_wo_variant[0].BaseSequence }.Count);
            Assert.IsTrue(proteins.All(p => p.FullName != null));
            Assert.IsTrue(proteins.Any(p => p.FullName.Contains(FunctionalClass.SILENT.ToString()))); // synonymous
            Assert.IsTrue(proteins.Any(p => p.FullName.Contains(GenotypeType.HOMOZYGOUS_ALT.ToString()))); // synonymous
            Assert.IsTrue(proteins.Any(p => p.FullName.Contains("1:69666")));

            string proteinFasta = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr_1_one_homozygous_synonymous.fasta");
            ProteinDbWriter.WriteFastaDatabase(proteins, proteinFasta, " ");
            string[] proteinFastaLines = File.ReadLines(proteinFasta).ToArray();
            Assert.IsTrue(proteinFastaLines[0].Contains(FunctionalClass.SILENT.ToString())); // synonymous
            Assert.IsTrue(proteinFastaLines[0].Contains("1:69666"));
        }

        [Test]
        public void TranslateReverseStrand()
        {
            Genome genome = new Genome(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_sample.fa"));
            GeneModel geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr1_one_transcript_reverse.gtf"));
            List<Protein> proteins_wo_variant = geneModel.Translate(true).ToList();
            Assert.AreEqual("FFYFIIWSLTLLPRAGLELLTSSDPPASASQSVGITGVSHHAQ",
                proteins_wo_variant[0].BaseSequence);
        }

        [Test]
        public void TranslateAnotherReverseStrand()
        {
            // See http://useast.ensembl.org/Homo_sapiens/Transcript/Sequence_cDNA?db=core;g=ENSG00000233306;r=7:38362864-38363518;t=ENST00000426402

            WrapperUtility.GenerateAndRunScript(Path.Combine(TestContext.CurrentContext.TestDirectory, "scripts", "chr7script.bash"), new List<string>
            {
                "cd " + WrapperUtility.ConvertWindowsPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData")),
                "if [ ! -f Homo_sapiens.GRCh38.dna.chromosome.7.fa ]; then wget ftp://ftp.ensembl.org/pub/release-91/fasta/homo_sapiens/dna/Homo_sapiens.GRCh38.dna.chromosome.7.fa.gz; fi",
                "if [ ! -f Homo_sapiens.GRCh38.dna.chromosome.7.fa ]; then gunzip Homo_sapiens.GRCh38.dna.chromosome.7.fa.gz; fi",
                WrapperUtility.EnsureClosedFileCommands(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Homo_sapiens.GRCh38.dna.chromosome.7.fa"))
            }).WaitForExit();
            Genome genome = new Genome(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Homo_sapiens.GRCh38.dna.chromosome.7.fa"));
            GeneModel geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr7_one_transcript_reverse.gtf"));
            List<Protein> proteins = geneModel.Translate(true).ToList();
            Assert.AreEqual("MQWALAVLLAFLSPASQKSSNLEGRTKSVIRQTGSSAEITCDLAEGSNGYIHWYLHQEGKAPQRLQYYDSYNSKVVLESGVSPGKYYTYASTRNNLRLILRNLIENDFGVYYCATWDG",
                proteins[0].BaseSequence);
        }

        [Test]
        public void TranslateSelenocysteineContaining()
        {
            WrapperUtility.GenerateAndRunScript(Path.Combine(TestContext.CurrentContext.TestDirectory, "scripts", "chr5script.bash"), new List<string>
            {
                "cd " + WrapperUtility.ConvertWindowsPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData")),
                "if [ ! -f Homo_sapiens.GRCh38.pep.all.fa ]; then wget ftp://ftp.ensembl.org/pub/release-81//fasta/homo_sapiens/pep/Homo_sapiens.GRCh38.pep.all.fa.gz; fi",
                "if [ ! -f Homo_sapiens.GRCh38.pep.all.fa ]; then gunzip " + EnsemblDownloadsWrapper.GRCh38ProteinFastaFilename + ".gz; fi",
                "if [ ! -f Homo_sapiens.GRCh38.dna.chromosome.5.fa ]; then wget ftp://ftp.ensembl.org/pub/release-91/fasta/homo_sapiens/dna/Homo_sapiens.GRCh38.dna.chromosome.5.fa.gz; fi",
                "if [ ! -f Homo_sapiens.GRCh38.dna.chromosome.5.fa ]; then gunzip Homo_sapiens.GRCh38.dna.chromosome.5.fa.gz; fi",
                WrapperUtility.EnsureClosedFileCommands(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Homo_sapiens.GRCh38.dna.chromosome.5.fa")),
                WrapperUtility.EnsureClosedFileCommands(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Homo_sapiens.GRCh38.pep.all.fa"))
            }).WaitForExit();
            Genome genome = new Genome(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Homo_sapiens.GRCh38.dna.chromosome.5.fa"));
            GeneModel geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chr5_selenocysteineContaining.gff3"));
            EnsemblDownloadsWrapper.GetImportantProteinAccessions(TestContext.CurrentContext.TestDirectory, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", EnsemblDownloadsWrapper.GRCh38ProteinFastaFilename), out var proteinSequences, out HashSet<string> badProteinAccessions, out Dictionary<string, string> selenocysteineContainingAccessions);
            List<Protein> proteins = geneModel.Translate(true, badProteinAccessions, selenocysteineContainingAccessions).ToList();
            Assert.AreEqual("MWRSLGLALALCLLPSGGTESQDQSSLCKQPPAWSIRDQDPMLNSNGSVTVVALLQASUYLCILQASKLEDLRVKLKKEGYSNISYIVVNHQGISSRLKYTHLKNKVSEHIPVYQQEENQTDVWTLLNGSKDDFLIYDRCGRLVYHLGLPFSFLTFPYVEEAIKIAYCEKKCGNCSLTTLKDEDFCKRVSLATVDKTVETPSPHYHHEHHHNHGHQHLGSSELSENQQPGAPNAPTHPAPPGLHHHHKHKGQHRQGHPENRDMPASEDLQDLQKKLCRKRCINQLLCKLPTDSELAPRSUCCHCRHLIFEKTGSAITUQCKENLPSLCSUQGLRAEENITESCQURLPPAAUQISQQLIPTEASASURUKNQAKKUEUPSN",
                proteins[0].BaseSequence);
        }

        [Test]
        public void TranslateMTSeq()
        {
            WrapperUtility.GenerateAndRunScript(Path.Combine(TestContext.CurrentContext.TestDirectory, "scripts", "chr7script.bash"), new List<string>
            {
                "cd " + WrapperUtility.ConvertWindowsPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData")),
                "if [ ! -f Homo_sapiens.GRCh38.dna.chromosome.MT.fa ]; then wget ftp://ftp.ensembl.org/pub/release-91/fasta/homo_sapiens/dna/Homo_sapiens.GRCh38.dna.chromosome.MT.fa.gz; fi",
                "if [ ! -f Homo_sapiens.GRCh38.dna.chromosome.MT.fa ]; then gunzip Homo_sapiens.GRCh38.dna.chromosome.MT.fa.gz; fi",
                WrapperUtility.EnsureClosedFileCommands(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Homo_sapiens.GRCh38.dna.chromosome.MT.fa"))
            }).WaitForExit();
            Genome genome = new Genome(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Homo_sapiens.GRCh38.dna.chromosome.MT.fa"));

            GeneModel geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chrM_one_transcript_reverse.gtf"));
            List<Protein> proteins = geneModel.Translate(true).ToList();
            Assert.AreEqual("MPMANLLLLIVPILIAMAFLMLTERKILGYMQLRKGPNVVGPYGLLQPFADAMKLFTKEPLKPATSTITLYITAPTLALTIALLLWTPLPMPNPLVNLNLGLLFILATSSLAVYSILWSGWASNSNYALIGALRAVAQTISYEVTLAIILLSTLLMSGSFNLSTLITTQEHLWLLLPSWPLAMMWFISTLAETNRTPFDLAEGESELVSGFNIEYAAGPFALFFMAEYTNIIMMNTLTTTIFLGTTYDALSPELYTTYFVTKTLLLTSLFLWIRTAYPRFRYDQLMHLLWKNFLPLTLALLMWYVSMPITISSIPPQT",
                proteins[0].BaseSequence);

            geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "chrM_one_transcript_reverse2.gtf"));
            proteins = geneModel.Translate(true).ToList();
            Assert.AreEqual("INPLAQPVIYSTIFAGTLITALSSHWFFTWVGLEMNMLAFIPVLTKKMNPRSTEAAIKYFLTQATASMILLMAILFNNMLSGQWTMTNTTNQYSSLMIMMAMAMKLGMAPFHFWVPEVTQGTPLTSGLLLLTWQKLAPISIMYQISPSLNVSLLLTLSILSIMAGSWGGLNQTQLRKILAYSSITHMGWMMAVLPYNPNMTILNLTIYIILTTTAFLLLNLNSSTTTLLLSRTWNKLTWLTPLIPSTLLSLGGLPPLTGFLPKWAIIEEFTKNNSLIIPTIMATITLLNLYFYLRLIYSTSITLLPMSNNVKMKWQFEHTKPTPFLPTLIALTTLLLPISPFMLMIL",
                proteins[0].BaseSequence);
        }

        [Test]
        public void ProblematicChr19Gene()
        {
            WrapperUtility.GenerateAndRunScript(Path.Combine(TestContext.CurrentContext.TestDirectory, "scripts", "chr19script.bash"), new List<string>
            {
                "cd " + WrapperUtility.ConvertWindowsPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData")),
                "if [ ! -f Homo_sapiens.GRCh38.dna.chromosome.19.fa ]; then wget ftp://ftp.ensembl.org/pub/release-91/fasta/homo_sapiens/dna/Homo_sapiens.GRCh38.dna.chromosome.19.fa.gz; fi",
                "if [ ! -f Homo_sapiens.GRCh38.dna.chromosome.19.fa ]; then gunzip Homo_sapiens.GRCh38.dna.chromosome.19.fa.gz; fi",
                WrapperUtility.EnsureClosedFileCommands(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Homo_sapiens.GRCh38.dna.chromosome.19.fa"))
            }).WaitForExit();
            Genome genome = new Genome(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Homo_sapiens.GRCh38.dna.chromosome.19.fa"));
            GeneModel geneModel = new GeneModel(genome, Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "problematicChr19", "problematicChr19Gene.gff3"));
            int problematic = geneModel.Genes.Sum(g => g.Transcripts.Count(t => t.RetrieveCodingSequence().Count % 3 != 0));
            geneModel.ApplyVariants(new VCFParser(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "problematicChr19", "chr19problematic.vcf")).Select(v => new Variant(null, v, genome.Chromosomes[0])).ToList());
        }

        // test todo: transcript with zero CodingSequenceExons and try to translate them to check that it doesn fail
        // test todo: multiple transcripts
    }
}