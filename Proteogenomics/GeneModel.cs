﻿using Bio;
using Bio.IO.Gff;
using Bio.VCF;
using Proteomics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Proteogenomics
{
    /// <summary>
    /// Contains representation of genes, transcripts, exons, etc. represented in a gene model. Can be amended with variants.
    /// </summary>
    public class GeneModel
    {
        /// <summary>
        /// Gets the first instance of a word
        /// </summary>
        private static Regex attributeKey = new Regex(@"([\w]+)");

        /// <summary>
        /// Gets anything inside quotes
        /// </summary>
        private static Regex attributeValue = new Regex(@"""([\w.]+)""");

        /// <summary>
        /// Constructs this GeneModel object from a Genome object and a corresponding GTF or GFF3 gene model file.
        /// </summary>
        /// <param name="genome"></param>
        /// <param name="geneModelFile"></param>
        public GeneModel(Genome genome, string geneModelFile)
        {
            Genome = genome;
            ReadGeneFeatures(geneModelFile);
        }

        /// <summary>
        /// Genome this gene model is based on.
        /// </summary>
        public Genome Genome { get; set; }

        /// <summary>
        /// Forest of intervals by chromosome name
        /// </summary>
        public IntervalForest GenomeForest { get; set; } = new IntervalForest();

        /// <summary>
        /// Genes represented
        /// </summary>
        public List<Gene> Genes { get; set; } = new List<Gene>();

        /// <summary>
        /// Intergenic regions
        /// </summary>
        public List<Intergenic> Intergenics { get; set; } = new List<Intergenic>();

        #region Methods -- Read Gene Model File

        private Gene currentGene = null;
        private Transcript currentTranscript = null;

        public void ReadGeneFeatures(string geneModelFile)
        {
            ForceGffVersionTo2(geneModelFile, out string geneModelWithVersion2MarkedPath);
            List<ISequence> geneFeatures = new GffParser().Parse(geneModelWithVersion2MarkedPath).ToList();

            foreach (ISequence chromFeatures in geneFeatures)
            {
                Chromosome chrom = Genome.Chromosomes.FirstOrDefault(x => x.FriendlyName == chromFeatures.ID);
                if (chrom == null) { continue; }
                chromFeatures.Metadata.TryGetValue("features", out object f);
                List<MetadataListItem<List<string>>> features = f as List<MetadataListItem<List<string>>>;
                for (int i = 0; i < features.Count; i++)
                {
                    MetadataListItem<List<string>> feature = features[i];
                    long.TryParse(feature.SubItems["start"][0], out long start);
                    long.TryParse(feature.SubItems["end"][0], out long end);

                    Dictionary<string, string> attributes = new Dictionary<string, string>();
                    foreach (string attrib in feature.FreeText.Split(';'))
                    {
                        string key;
                        string val;
                        if (feature.FreeText.Contains('=')) // GFF3
                        {
                            key = attrib.Split('=')[0].TrimStart();
                            val = attrib.Split('=')[1].TrimStart();
                        }
                        else // GFF1 or GTF
                        {
                            key = attributeKey.Match(attrib.TrimStart()).Groups[1].Value;
                            val = attributeValue.Match(attrib.TrimStart()).Groups[1].Value;
                        }

                        if (!attributes.TryGetValue(key, out string x)) // sometimes there are two tags, so avoid adding twice
                        {
                            attributes.Add(key, val);
                        }
                    }

                    if (feature.FreeText.Contains('='))
                    {
                        ProcessGff3Feature(feature, start, end, chrom, attributes);
                    }
                    else
                    {
                        ProcessGtfFeature(feature, start, end, chrom, attributes);
                    }
                }
            }
            CreateUTRsAndIntergenicRegions();
            // possibly check transcript sanity here with Parallel.ForEach(Genes.SelectMany(g => g.Transcripts).ToList(), t => t.SanityCheck());
            foreach (Gene gene in Genes)
            {
                gene.TranscriptTree.Build();
            }
            GenomeForest.Build();
        }

        /// <summary>
        /// Processes a feature from a GFF3 gene model file.
        /// </summary>
        /// <param name="feature"></param>
        /// <param name="oneBasedStart"></param>
        /// <param name="oneBasedEnd"></param>
        /// <param name="chrom"></param>
        /// <param name="attributes"></param>
        public void ProcessGff3Feature(MetadataListItem<List<string>> feature, long oneBasedStart, long oneBasedEnd, Chromosome chrom, Dictionary<string, string> attributes)
        {
            bool hasGeneId = attributes.TryGetValue("gene_id", out string geneId);
            bool hasTranscriptId = attributes.TryGetValue("transcript_id", out string transcriptId);
            bool hasTranscriptVersion = attributes.TryGetValue("version", out string transcriptVersion) && hasTranscriptId;
            bool hasExonId = attributes.TryGetValue("exon_id", out string exonId);
            bool hasProteinId = attributes.TryGetValue("protein_id", out string proteinId);
            bool hasStrand = feature.SubItems.TryGetValue("strand", out List<string> strandish);
            if (!hasStrand)
            {
                return;
            }
            string strand = strandish[0];

            if (hasGeneId && (currentGene == null || hasGeneId && geneId != currentGene.ID))
            {
                currentGene = new Gene(geneId, chrom, strand, oneBasedStart, oneBasedEnd, feature);
                Genes.Add(currentGene);
                GenomeForest.Add(currentGene);
            }

            if (hasTranscriptId && (currentTranscript == null || hasTranscriptId && transcriptId != currentTranscript.ID))
            {
                currentTranscript = new Transcript(transcriptId, transcriptVersion, currentGene, strand, oneBasedStart, oneBasedEnd, null, null);
                currentGene.Transcripts.Add(currentTranscript);
                currentGene.TranscriptTree.Add(currentTranscript);
                GenomeForest.Add(currentTranscript);
            }

            if (hasExonId)
            {
                ISequence exon_dna = chrom.Sequence.GetSubSequence(oneBasedStart - 1, oneBasedEnd - oneBasedStart + 1);
                Exon exon = new Exon(currentTranscript, currentTranscript.IsStrandPlus() ? exon_dna : exon_dna.GetReverseComplementedSequence(),
                    oneBasedStart, oneBasedEnd, chrom == null ? "" : chrom.ChromosomeID, strand, null);
                currentTranscript.Exons.Add(exon);
            }
            else if (hasProteinId)
            {
                CDS cds = new CDS(currentTranscript, chrom.Sequence.ID, strand, oneBasedStart, oneBasedEnd, null);
                currentTranscript.CodingDomainSequences.Add(cds);
                currentTranscript.ProteinID = proteinId;
            }
            else
            { // nothing to do
            }
        }

        /// <summary>
        /// Processes a feature from a GTF gene model file.
        /// </summary>
        /// <param name="feature"></param>
        /// <param name="oneBasedStart"></param>
        /// <param name="oneBasedEnd"></param>
        /// <param name="chrom"></param>
        /// <param name="attributes"></param>
        public void ProcessGtfFeature(MetadataListItem<List<string>> feature, long oneBasedStart, long oneBasedEnd, Chromosome chrom, Dictionary<string, string> attributes)
        {
            bool hasGeneId = attributes.TryGetValue("gene_id", out string geneId);
            bool hasGeneName = attributes.TryGetValue("gene_name", out string geneName);
            bool hasGeneVersion = attributes.TryGetValue("gene_version", out string geneVersion);
            bool hasGeneBiotype = attributes.TryGetValue("gene_biotype", out string geneBiotype);
            bool hasTranscriptId = attributes.TryGetValue("transcript_id", out string transcriptId);
            bool hasTranscriptVersion = attributes.TryGetValue("transcript_version", out string transcriptVersion);
            bool hasTranscriptBiotype = attributes.TryGetValue("transcript_biotype", out string transcriptBiotype);
            bool hasExonId = attributes.TryGetValue("exon_id", out string exonId);
            bool hasExonVersion = attributes.TryGetValue("exon_version", out string exonVersion);
            bool hasExonNumber = attributes.TryGetValue("exon_number", out string exonNumber);
            bool hasNearestRef = attributes.TryGetValue("nearest_ref", out string nearestRef); // Cufflinks
            bool hasClassCode = attributes.TryGetValue("class_code", out string classCode); // Cufflinks
            string strand = feature.SubItems["strand"][0];

            // Catch the transcript features before they go by if available, i.e. if the file doesn't just have exons
            if (feature.Key == "transcript" && (currentTranscript == null || hasTranscriptId && transcriptId != currentTranscript.ID))
            {
                if (currentGene == null || hasGeneId && geneId != currentGene.ID)
                {
                    currentGene = new Gene(geneId, chrom, strand, oneBasedStart, oneBasedEnd, feature);
                    Genes.Add(currentGene);
                    GenomeForest.Add(currentGene);
                }

                currentTranscript = new Transcript(transcriptId, transcriptVersion, currentGene, strand, oneBasedStart, oneBasedEnd, null, null);
                currentGene.Transcripts.Add(currentTranscript);
                currentGene.TranscriptTree.Add(currentTranscript);
                GenomeForest.Add(currentTranscript);
            }

            if (feature.Key == "exon" || feature.Key == "CDS")
            {
                if (currentGene == null || hasGeneId && geneId != currentGene.ID)
                {
                    currentGene = new Gene(geneId, chrom, strand, oneBasedStart, oneBasedEnd, feature);
                    Genes.Add(currentGene);
                    GenomeForest.Add(currentGene);
                }

                if (currentTranscript == null || hasTranscriptId && transcriptId != currentTranscript.ID)
                {
                    currentTranscript = new Transcript(transcriptId, transcriptVersion, currentGene, strand, oneBasedStart, oneBasedEnd, null, null);
                    currentGene.Transcripts.Add(currentTranscript);
                    GenomeForest.Add(currentTranscript);
                }

                if (feature.Key == "exon")
                {
                    ISequence exon_dna = chrom.Sequence.GetSubSequence(oneBasedStart - 1, oneBasedEnd - oneBasedStart + 1);
                    Exon exon = new Exon(currentTranscript, currentTranscript.IsStrandPlus() ? exon_dna : exon_dna.GetReverseComplementedSequence(),
                        oneBasedStart, oneBasedEnd, chrom.Sequence.ID, strand, null);
                    currentTranscript.Exons.Add(exon);
                }
                else if (feature.Key == "CDS")
                {
                    CDS cds = new CDS(currentTranscript, chrom.Sequence.ID, strand, oneBasedStart, oneBasedEnd, null);
                    currentTranscript.CodingDomainSequences.Add(cds);
                }
                else
                { // nothing to do
                }
            }
        }

        private static Regex gffVersion = new Regex(@"(##gff-version\s+)(\d)");

        /// <summary>
        /// Required for using DotNetBio because it only handles GFF version 2 in the header.
        /// The only difference in the new version is within the attributes, which are stored as free text anyway.
        /// </summary>
        /// <param name="gffPath"></param>
        /// <param name="gffWithVersionMarked2Path"></param>
        private static void ForceGffVersionTo2(string gffPath, out string gffWithVersionMarked2Path)
        {
            gffWithVersionMarked2Path = Path.Combine(Path.GetDirectoryName(gffPath), Path.GetFileNameWithoutExtension(gffPath) + ".gff2" + Path.GetExtension(gffPath));
            if (File.Exists(gffWithVersionMarked2Path)) return;

            using (StreamReader reader = new StreamReader(gffPath))
            using (StreamWriter writer = new StreamWriter(gffWithVersionMarked2Path))
            {
                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    if (line.StartsWith("##gff-version"))
                    {
                        writer.Write(gffVersion.Replace(line, m => m.Groups[1] + "2") + "\n");
                    }
                    else
                    {
                        writer.Write(line + '\n');
                    }
                }
            }
        }

        #endregion Methods -- Read Gene Model File

        #region Methods -- Applying Variants

        /// <summary>
        /// Creates UTRs for transcripts and intergenic regions after reading gene model
        /// </summary>
        public void CreateUTRsAndIntergenicRegions()
        {
            foreach (IntervalTree it in GenomeForest.Forest.Values)
            {
                Gene previousPositiveStrandGene = null;
                Gene previousNegativeStrandGene = null;

                // Create intergenic regions on each strand
                foreach (Gene gene in it.Intervals.OfType<Gene>().OrderBy(g => g.OneBasedStart))
                {
                    Intergenic intergenic = null;
                    Gene previous = gene.IsStrandPlus() ? previousPositiveStrandGene : previousNegativeStrandGene;
                    if (previous != null)
                    {
                        // if there's a previous gene, create the intergenic region
                        intergenic = new Intergenic(gene.Chromosome, gene.ChromosomeID, gene.Strand, previous.OneBasedEnd + 1, gene.OneBasedStart - 1, null);
                    }

                    // store previous genes on each strand
                    if (gene.IsStrandPlus())
                    {
                        previousPositiveStrandGene = gene;
                    }
                    if (gene.IsStrandMinus())
                    {
                        previousNegativeStrandGene = gene;
                    }

                    // add the intergenic region to the genome forest if it was created
                    if (intergenic != null && intergenic.Length() > 0)
                    {
                        GenomeForest.Add(intergenic);
                    }

                    // while we're here, set the regions of each transcript, too
                    foreach (Transcript t in gene.Transcripts)
                    {
                        Transcript.SetRegions(t);
                    }
                }
            }
        }

        /// <summary>
        /// Apply a list of variants to the intervals within this gene model
        /// </summary>
        /// <param name="variants"></param>
        public List<Transcript> ApplyVariants(List<Variant> variants)
        {
            // first, add variants to relevant genomic regions
            foreach (Variant v in variants.OrderByDescending(v => v.OneBasedStart).ToList())
            {
                List<Interval> intervals = GenomeForest.Forest[Chromosome.GetFriendlyChromosomeName(v.ChromosomeID)].Stab(v.OneBasedStart);
                foreach (Interval i in intervals)
                {
                    i.Variants.Add(v);
                }
            }

            // then, apply them
            return Genes.SelectMany(g => g.Transcripts)
                .SelectMany(t => ApplyVariantsCombinitorially(t)).ToList();
        }

        /// <summary>
        /// Apply variants to a transcript
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static List<Transcript> ApplyVariantsCombinitorially(Transcript t)
        {
            List<Transcript> newTranscripts = new List<Transcript> { new Transcript(t) };
            if (t.Variants.Count(v => v.GenotypeType == GenotypeType.HETEROZYGOUS) > 5) // avoid large combinitoric problems for now (heterozygous count > 5)
            {
                Transcript.combinatoricFailures.Add(t.ID + " " + t.ProteinID);
                return newTranscripts;
            }
            List<Variant> transcriptVariants = t.Variants.OrderByDescending(v => v.OneBasedStart).ToList(); // reversed, so that the coordinates of each successive variant is not changed
            foreach (Variant v in transcriptVariants)
            {
                newTranscripts = newTranscripts.SelectMany(nt => nt.ApplyVariantCombinitorics(v)).ToList(); // expands only when there is a heterozygous nonsynonymous variation
            }
            return newTranscripts;
        }

        #endregion Methods -- Applying Variants

        #region Translation Methods

        public List<Protein> Translate(bool translateCodingDomains, HashSet<string> incompleteTranscriptAccessions = null, Dictionary<string, string> selenocysteineContaining = null)
        {
            return Genes.SelectMany(g => g.Translate(translateCodingDomains, incompleteTranscriptAccessions, selenocysteineContaining)).ToList();
        }

        public List<Protein> TranslateUsingAnnotatedStartCodons(GeneModel genesWithCodingDomainSequences, Dictionary<string, string> selenocysteineContaining, int minPeptideLength = 7)
        {
            return Genes.SelectMany(g => g.TranslateUsingAnnotatedStartCodons(genesWithCodingDomainSequences, selenocysteineContaining, minPeptideLength)).ToList();
        }

        #endregion Translation Methods
    }
}