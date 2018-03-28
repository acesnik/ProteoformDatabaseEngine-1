﻿using Bio;
using Bio.Algorithms.Translation;
using Bio.Extensions;
using Bio.VCF;
using Proteomics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Proteogenomics
{
    public class Transcript
        : Interval
    {
        /// <summary>
        /// Used to construct upstream and downstream reginos
        /// </summary>
        public static readonly int DEFAULT_UP_DOWN_LENGTH = 5000;

        /// <summary>
        /// Keeping track of transcript IDs leading to failures in executing combinitorics
        /// </summary>
        public static List<string> combinatoricFailures = new List<string>();

        /// <summary>
        /// Constructor from the GFF3 reader information, including IDs, strand and Protein ID if available.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="gene"></param>
        /// <param name="metadata"></param>
        /// <param name="ProteinID"></param>
        public Transcript(string id, string version, Gene gene, string strand, long oneBasedStart, long oneBasedEnd, string proteinID)
            : base(gene, gene.ChromosomeID, strand, oneBasedStart, oneBasedEnd)
        {
            ID = id;
            Version = version;
            ProteinID = proteinID ?? id;
            Gene = gene;
        }

        /// <summary>
        /// The transcript ID
        /// </summary>
        public string ID { get; set; }

        /// <summary>
        /// The transcript version
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// The parent gene containing this transcript.
        /// </summary>
        public Gene Gene { get; set; }

        /// <summary>
        /// A list of exons, possibly including untranslated regions (UTRs).
        /// </summary>
        public List<Exon> Exons { get; set; } = new List<Exon>();

        /// <summary>
        /// Exons sorted by start position or by reverse end position if on reverse strand
        /// </summary>
        public List<Exon> ExonsSortedStrand { get { return SortedStrand(); } }

        /// <summary>
        /// List of the untranslated regions
        /// </summary>
        public List<UTR> UTRs { get; set; } = new List<UTR>();

        /// <summary>
        /// Downstream region, 5 kb by default
        /// </summary>
        public Downstream Downstream { get; set; }

        /// <summary>
        /// Upstream region, 5 kb by default
        /// </summary>
        public Upstream Upstream { get; set; }

        /// <summary>
        /// Introns for this transcript
        /// </summary>
        public List<Intron> Introns { get; set; }

        /// <summary>
        /// Coding domain sequence (CDS) information
        /// </summary>
        public List<CDS> CodingDomainSequences { get; set; } = new List<CDS>();

        /// <summary>
        /// Coding sequence
        /// </summary>
        public ISequence CodingSequence { get; set; }

        public long cdsStart { get; set; }
        public long cdsEnd { get; set; }
        public long[] cds2pos { get; set; }
        public long[] aa2pos { get; set; }

        /// <summary>
        /// Variants annotated for this transcript using SnpEff.
        /// </summary>
        public List<SnpEffAnnotation> SnpEffVariants { get; set; } = new List<SnpEffAnnotation>();

        /// <summary>
        /// The protein ID derived from coding transcripts; this is imported from the GFF3 file if available.
        /// </summary>
        public string ProteinID { get; set; }

        /// <summary>
        /// Annotations for the variants applied to this transcript
        /// </summary>
        public List<string> VariantAnnotations { get; set; } = new List<string>();

        /// <summary>
        /// Apply the second (alternate) allele of this variant and adjust the start and stop indices
        /// </summary>
        /// <param name="variant"></param>
        /// <returns></returns>
        public override Interval ApplyVariant(Variant variant)
        {
            Interval interval = base.ApplyVariant(variant);
            Transcript transcript = new Transcript(ID, Version, Gene, interval.Strand, interval.OneBasedStart, interval.OneBasedEnd, ProteinID);
            for (int i = 0; i < CodingDomainSequences.Count; i++)
            {
                if (CodingDomainSequences[i].Includes(variant))
                {
                    transcript.CodingDomainSequences.Add(CodingDomainSequences[i].ApplyVariant(variant) as CDS);
                }
                transcript.CodingDomainSequences.Add(CodingDomainSequences[i]);
            }
            for (int i = 0; i < Exons.Count; i++)
            {
                if (Exons[i].Includes(variant))
                {
                    transcript.Exons.Add(Exons[i].ApplyVariant(variant) as Exon); // applies variant to sequence
                }
                transcript.Exons.Add(Exons[i]);
            }

            transcript.VariantAnnotations = new List<string>(VariantAnnotations);
            transcript.CreateIntrons();
            transcript.CreateUTRs();
            transcript.CreateUpDown(Gene.Chromosome);
            return transcript;
        }

        /// <summary>
        /// Applies the first allele, for when it doesn't match the reference
        /// </summary>
        /// <param name="variant"></param>
        /// <returns></returns>
        private Interval ApplyFirstAllele(Variant variant)
        {
            Variant v = new Variant(variant.Parent, variant.VariantContext, variant.Chromosome);
            v.SecondAllele = v.FirstAllele;
            v.SecondAlleleDepth = v.FirstAlleleDepth;
            v.SecondAlleleString = v.FirstAlleleString;
            return ApplyVariant(v);
        }

        /// <summary>
        /// Takes in list of variants to combinitorially apply to this transcript
        /// </summary>
        /// <param name="variantOrderedDescStart"></param>
        /// <returns></returns>
        public IEnumerable<Transcript> ApplyVariantsCombinitorially(List<Variant> variantOrderedDescStart)
        {
            List<Transcript> result = new List<Transcript>();
            for (int i = 0; i < variantOrderedDescStart.Count; i++)
            {
                Variant v = variantOrderedDescStart[i];
                VariantEffects variantEffects = AnnotateVariant(v);
                bool missenseOrNonsense = variantEffects.Effects.Any(eff => eff.getFunctionalClass().CompareTo(FunctionalClass.MISSENSE) >= 0);
                if (variantEffects == null)
                {
                    continue;
                }
                Transcript firstAllele = this;
                Transcript secondAllele = ApplyVariant(v) as Transcript;
                secondAllele.VariantAnnotations.Add(variantEffects.TranscriptAnnotation());
                result.Add(secondAllele);
                if (variantOrderedDescStart.Count - i > 1)
                {
                    // add the combinations of the new transcript with the remaining variants (recurse)
                    result.AddRange(secondAllele.ApplyVariantsCombinitorially(variantOrderedDescStart.GetRange(i + 1, variantOrderedDescStart.Count - i - 1)));
                }
                if (v.GenotypeType == GenotypeType.HETEROZYGOUS && missenseOrNonsense)
                {
                    // if the first allele is different than the reference (possible error in reference sequence), then make that change
                    if (v.ReferenceAlleleString != v.FirstAlleleString)
                    {
                        firstAllele = ApplyFirstAllele(v) as Transcript;
                    }
                    // if heterozygous and a coding change, add the first allele, too
                    result.Add(firstAllele);
                }
                if (v.GenotypeType == GenotypeType.HETEROZYGOUS && missenseOrNonsense && variantOrderedDescStart.Count - i > 1)
                {
                    // if heterozygous and a coding change, add the first allele with combinations of the remaining variants (recurse)
                    result.AddRange(firstAllele.ApplyVariantsCombinitorially(variantOrderedDescStart.GetRange(i + 1, variantOrderedDescStart.Count - i - 1)));
                }
            }
            return result;
        }

        /// <summary>
        /// Gets a string representing a variant applied to this transcript
        /// </summary>
        /// <param name="variant"></param>
        public VariantEffects AnnotateVariant(Variant variant)
        {
            // Then in translation, make a simple method to look up the bad accessions
            //      (could also try to assess this from the sequence itself using the warnings and errors)
            //      (namely, does it have stop codons in it, does it have a start codon)
            //      (but will have to do this anyway to find the selenocysteine sequences, so might as well just keep that code)

            // Test
            //   1. UTR ranges get change
            //   2. Correct UTR gets changed (5' or 3')
            //   3. Variants get applied correctly
            //   Uh, lots more.

            if (!Intersects(variant)) { return null; } // Sanity check

            VariantEffects variantEffects = new VariantEffects();

            // Large structural variant including the whole transcript?
            if (variant.Includes(this) && variant.isStructural())
            {
                CodonChange codonChange = CodonChange.Factory(variant, this, variantEffects);
                codonChange.ChangeCodon();
                return variantEffects;
            }

            //---
            // Structural variants may affect more than one exon
            //---
            bool mayAffectSeveralExons = variant.isStructural() || variant.isMixed() || variant.isMnv();
            if (mayAffectSeveralExons)
            {
                int countExon = 0;
                foreach (Exon ex in Exons)
                {
                    if (ex.Intersects(variant))
                    {
                        countExon++;
                    }
                }

                // More than one exon?
                if (countExon > 1)
                {
                    CodonChange codonChange = CodonChange.Factory(variant, this, variantEffects);
                    codonChange.ChangeCodon();
                    return variantEffects;
                }
            }

            //---
            // Does it hit an exon?
            // Note: This only adds spliceSites effects, for detailed codon
            //       changes effects we use 'CodonChange' class
            //---
            bool exonAnnotated = false;
            foreach (Exon ex in Exons)
            {
                if (ex.Intersects(variant))
                {
                    exonAnnotated |= ex.VariantEffect(variant, variantEffects);
                }
            }

            //---
            // Hits a UTR region?
            //---
            bool included = false;
            foreach (UTR utr in UTRs)
            {
                if (utr.Intersects(variant))
                {
                    // Calculate the effect
                    utr.VariantEffect(variant, variantEffects);
                    included |= utr.Includes(variant); // Is this variant fully included in the UTR?
                }
            }
            if (included)
            {
                return variantEffects; // Variant fully included in the UTR? => We are done.
            }

            //---
            // Does it hit an intron?
            //---
            foreach (Intron intron in Introns)
            {
                if (intron.Intersects(variant))
                {
                    intron.VariantEffect(variant, variantEffects);
                    included |= intron.Includes(variant); // Is this variant fully included in this intron?
                }
            }
            if (included)
            {
                return variantEffects; // Variant fully included? => We are done.
            }

            //---
            // No annotations from exons? => Add transcript
            //---
            if (!exonAnnotated)
            {
                variantEffects.add(SetEffect(variant, EffectType.TRANSCRIPT));
                return variantEffects;
            }

            return variantEffects;
        }

        private VariantEffect SetEffect(Variant variant, EffectType type)
        {
            VariantEffect ve = new VariantEffect(variant);
            ve.addErrorWarningInfo(sanityCheck(variant));
            Exon x = FindExon(variant);
            if (x != null)
            {
                ve.addErrorWarningInfo(x.SanityCheck(variant));
            }
            ve.setEffectType(type);
            ve.setEffectImpact(Proteogenomics.VariantEffect.EffectDictionary[type]);
            return ve;
        }

        /// <summary>
        /// Find base at genomic coordinate 'pos'
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public ISequence BaseAt(int pos)
        {
            CalcCdsStartEnd();
            Exon ex = FindExon(pos);
            if (ex == null) { return null; }
            return ex.basesAt(pos - ex.OneBasedStart, 1);
        }

        /// <summary>
        /// Calculate distance from transcript start to a position
        /// mRNA is roughly the same than cDNA.Strictly speaking mRNA
        /// has a poly-A tail and 5'cap.
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public long BaseNumber2MRnaPos(long pos)
        {
            long count = 0;
            foreach (Exon eint in ExonsSortedStrand)
            {
                if (eint.Intersects(pos))
                {
                    // Intersect this exon? Calculate the number of bases from the beginning
                    long dist = IsStrandPlus() ?
                        pos - eint.OneBasedStart :
                        eint.OneBasedEnd - pos;

                    // Sanity check
                    if (dist < 0)
                    {
                        throw new ArgumentException("Negative distance for position " + pos + ". This should never happen!\n" + this);
                    }

                    return count + dist;
                }

                count += eint.Length();
            }
            return -1;
        }

        /// <summary>
        /// Calculate base number in a CDS where 'pos' maps
        ///
        /// usePrevBaseIntron: When 'pos' is intronic this method returns:
        /// 			- if(usePrevBaseIntron== false)  => The first base in the exon after 'pos' (i.e.first coding base after intron)
        /// 			- if(usePrevBaseIntron== true)   => The last base in the exon before 'pos'  (i.e.last coding base before intron)
        ///
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="usePrevBaseIntron"></param>
        /// <returns>Base number or '-1' if it does not map to a coding base</returns>
        public long BaseNumberCds(long pos, bool usePrevBaseIntron)
        {
            // Doesn't hit this transcript?
            if (!Intersects(pos)) { return -1; }

            // Is it in UTR instead of CDS?
            if (UTRs.Any(utr => utr.Intersects(pos))) { return -1; }

            // Calculate cdsStart and cdsEnd (if not already done)
            CalcCdsStartEnd();

            // All exons..
            long firstCdsBaseInExon = 0; // Where the exon maps to the CDS (i.e. which CDS base number does the first base in this exon maps to).
            foreach (Exon eint in ExonsSortedStrand)
            {
                if (eint.Intersects(pos))
                {
                    long cdsBaseInExon = IsStrandPlus() ? // cdsBaseInExon: base number relative to the beginning of the coding part of this exon (i.e. excluding 5'UTRs)
                        pos - Math.Max(eint.OneBasedStart, cdsStart) :
                        Math.Min(eint.OneBasedEnd, cdsStart) - pos;

                    cdsBaseInExon = Math.Max(0, cdsBaseInExon);

                    return firstCdsBaseInExon + cdsBaseInExon;
                }
                else
                {
                    // Before exon begins?
                    if (IsStrandPlus() && pos < eint.OneBasedStart // Before exon begins (positive strand)?
                            || IsStrandMinus() && pos > eint.OneBasedEnd) // Before exon begins (negative strand)?
                        return firstCdsBaseInExon - (usePrevBaseIntron ? 1 : 0);
                }

                firstCdsBaseInExon += IsStrandPlus() ?
                    Math.Max(0, eint.OneBasedEnd - Math.Max(eint.OneBasedStart, cdsStart) + 1) :
                    Math.Max(0, Math.Min(cdsStart, eint.OneBasedEnd) - eint.OneBasedStart + 1);
            }

            return firstCdsBaseInExon - 1;
        }

        /// <summary>
        /// Return a codon that includes 'cdsBaseNumber'
        /// </summary>
        /// <param name="cdsBaseNumber"></param>
        /// <returns></returns>
        public string BaseNumberCds2Codon(int cdsBaseNumber)
        {
            int codonNum = cdsBaseNumber / CodonChange.CODON_SIZE;
            int min = codonNum * CodonChange.CODON_SIZE;
            int max = codonNum * CodonChange.CODON_SIZE + CodonChange.CODON_SIZE;
            if (min >= 0 && max <= RetrieveCodingSequence().Count)
            {
                return SequenceExtensions.ConvertToString(RetrieveCodingSequence().GetSubSequence(min, max)).ToUpper(CultureInfo.InvariantCulture);
            }
            return null;
        }

        /// <summary>
        /// Calculate chromosome position as function of CDS number
        /// </summary>
        /// <returns>An array mapping 'cds2pos[cdsBaseNumber] = chromosmalPos'</returns>
        public long[] BaseNumberCds2Pos()
        {
            if (cds2pos != null) { return cds2pos; }

            CalcCdsStartEnd();

            cds2pos = new long[RetrieveCodingSequence().Count];
            for (int i = 0; i < cds2pos.Length; i++)
            {
                cds2pos[i] = -1;
            }

            long cdsMin = Math.Min(cdsStart, cdsEnd);
            long cdsMax = Math.Max(cdsStart, cdsEnd);

            // For each exon, add CDS position to array
            int cdsBaseNum = 0;
            foreach (Exon exon in ExonsSortedStrand)
            {
                long min = IsStrandPlus() ? exon.OneBasedStart : exon.OneBasedEnd;
                int step = IsStrandPlus() ? 1 : -1;
                for (long pos = min; exon.Intersects(pos) && cdsBaseNum < cds2pos.Length; pos += step)
                {
                    if (cdsMin <= pos && pos <= cdsMax)
                    {
                        cds2pos[cdsBaseNum++] = pos;
                    }
                }
            }

            return cds2pos;
        }

        /// <summary>
        /// Calculate CDS start and CDS end
        /// </summary>
        private void CalcCdsStartEnd()
        {
            // Do we need to calculate these values?
            // Note: In circular genomes, one of cdsStart / cdsEnd might be less
            //       than zero (we must check both)
            if (cdsStart < 0 && cdsEnd < 0)
            {
                // Calculate coding start (after 5 prime UTR)

                if (UTRs.Count == 0)
                {
                    // No UTRs => Use all exons
                    cdsStart = IsStrandPlus() ? OneBasedEnd : OneBasedStart; // cdsStart is the position of the first base in the CDS (i.e. the first base after all 5'UTR)
                    cdsEnd = IsStrandPlus() ? OneBasedStart : OneBasedEnd; // cdsEnd is the position of the last base in the CDS (i.e. the first base before all 3'UTR)

                    foreach (Exon ex in Exons)
                    {
                        if (IsStrandPlus())
                        {
                            cdsStart = Math.Min(cdsStart, ex.OneBasedStart);
                            cdsEnd = Math.Max(cdsEnd, ex.OneBasedEnd);
                        }
                        else
                        {
                            cdsStart = Math.Max(cdsStart, ex.OneBasedEnd);
                            cdsEnd = Math.Min(cdsEnd, ex.OneBasedStart);
                        }
                    }
                }
                else
                {
                    // We have to take into account UTRs
                    cdsStart = IsStrandPlus() ? OneBasedStart : OneBasedEnd; // cdsStart is the position of the first base in the CDS (i.e. the first base after all 5'UTR)
                    cdsEnd = IsStrandPlus() ? OneBasedEnd : OneBasedStart; // cdsEnd is the position of the last base in the CDS (i.e. the first base before all 3'UTR)
                    long cdsStartNotExon = cdsStart;

                    foreach (UTR utr in UTRs)
                    {
                        if (utr is UTR5Prime)
                        {
                            if (IsStrandPlus()) { cdsStart = Math.Max(cdsStart, utr.OneBasedEnd + 1); }
                            else { cdsStart = Math.Min(cdsStart, utr.OneBasedStart - 1); }
                        }
                        else if (utr is UTR3Prime)
                        {
                            if (IsStrandPlus()) { cdsEnd = Math.Min(cdsEnd, utr.OneBasedStart - 1); }
                            else { cdsEnd = Math.Max(cdsEnd, utr.OneBasedEnd + 1); }
                        }
                    }

                    // Make sure cdsStart and cdsEnd lie within an exon
                    if (IsStrandPlus())
                    {
                        cdsStart = FirstExonPositionAfter(cdsStart);
                        cdsEnd = LastExonPositionBefore(cdsEnd);
                    }
                    else
                    {
                        cdsStart = LastExonPositionBefore(cdsStart);
                        cdsEnd = FirstExonPositionAfter(cdsEnd);
                    }

                    // We were not able to find cdsStart & cdsEnd within exon limits.
                    // Probably there is something wrong with the database and the transcript does
                    // not have a single coding base (e.g. all of it is UTR).
                    if (cdsStart < 0 || cdsEnd < 0)
                    {
                        cdsStart = cdsEnd = cdsStartNotExon;
                    }
                }
            }
        }

        /// <summary>
        /// Create a marker of the coding region in this transcript
        /// </summary>
        /// <returns></returns>
        public Interval CdsMarker()
        {
            Interval interval = new Interval(this);
            interval.OneBasedStart = IsStrandPlus() ? cdsStart : cdsEnd;
            interval.OneBasedEnd = IsStrandPlus() ? cdsEnd : cdsStart;
            return interval;
        }

        /// <summary>
        /// Find a CDS that matches exactly the exon
        /// </summary>
        /// <param name="exon"></param>
        /// <returns></returns>
        public CDS FindCds(Exon exon)
        {
            return CodingDomainSequences.FirstOrDefault(c => exon.Includes(c));
        }

        /// <summary>
        /// Return the an exon that intersects 'pos'
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public Exon FindExon(int pos)
        {
            return Exons.FirstOrDefault(x => x.Includes(pos));
        }

        /// <summary>
        /// Return an exon intersecting 'marker' (first exon found)
        /// </summary>
        /// <param name="marker"></param>
        /// <returns></returns>
        public Exon FindExon(Interval marker)
        {
            return Exons.FirstOrDefault(x => x.Intersects(marker));
        }

        /// <summary>
        /// Find the first position after 'pos' within an exon
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        private long FirstExonPositionAfter(long pos)
        {
            foreach (Exon ex in Exons.OrderBy(x => x.OneBasedStart))
            {
                if (pos <= ex.OneBasedStart) { return ex.OneBasedStart; }
                if (pos <= ex.OneBasedEnd) { return pos; }
            }

            Console.WriteLine("WARNING: Cannot find first exonic position after " + pos + " for transcript '" + ID + "'");
            return -1;
        }

        /// <summary>
        /// Find the last position before 'pos' within an exon
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        private long LastExonPositionBefore(long pos)
        {
            long last = -1;
            foreach (Exon ex in Exons.OrderBy(x => x.OneBasedStart))
            {
                if (pos < ex.OneBasedStart)
                {
                    // Nothing found?
                    if (last < 0)
                    {
                        Console.WriteLine("WARNING: Cannot find last exonic position before " + pos + " for transcript '" + ID + "'");
                        return -1;
                    }
                    return last;
                }
                else if (pos <= ex.OneBasedEnd)
                {
                    return pos;
                }
                last = ex.OneBasedEnd;
            }

            if (last < 0)
            {
                Console.WriteLine("WARNING: Cannot find last exonic position before " + pos + " for transcript '" + ID + "'");
            }
            return pos;
        }

        #region Translate from Variant Nucleotide Sequences Methods

        /// <summary>
        /// Get the coding sequence for this transcript.
        /// SnpEff keeps track of the UTRs to figure this out. I suppose that will work, now that I'm using the interval tree to dive down to change those ranges.
        /// </summary>
        /// <returns></returns>
        public ISequence RetrieveCodingSequence()
        {
            if (CodingSequence != null)
            {
                return CodingSequence;
            }

            // Concatenate all exons
            List<Exon> exons = ExonsSortedStrand;
            StringBuilder sequence = new StringBuilder();
            int utr5len = 0, utr3len = 0;

            // 5 prime UTR length
            foreach (UTR utr in UTRs.OfType<UTR5Prime>())
            {
                utr5len += (int)utr.Length();
            }

            // Append all exon sequences
            IAlphabet alphabet = null;
            bool missingSequence = false;
            foreach (Exon exon in exons)
            {
                missingSequence |= exon.Sequence != null; // If there is no sequence, we are in trouble
                sequence.Append(IsStrandPlus() ? exon.Sequence : exon.Sequence.GetReverseComplementedSequence());
                alphabet = alphabet == null || alphabet.HasAmbiguity && !exon.Sequence.Alphabet.HasAmbiguity ? // keep the alphabet with the most characters
                    alphabet :
                    exon.Sequence.Alphabet;
            }

            if (missingSequence)
            {
                CodingSequence = new Sequence(Alphabets.DNA, ""); // One or more exons does not have sequence. Nothing to do
            }
            else
            {
                // OK, all exons have sequences

                // 3 prime UTR length
                foreach (UTR utr in UTRs.OfType<UTR3Prime>())
                {
                    utr3len += (int)utr.Length();
                }

                // Cut 5 prime UTR and 3 prime UTR points
                int subEnd = sequence.Length - utr3len;

                if (utr5len > subEnd)
                {
                    CodingSequence = new Sequence(Alphabets.DNA, "");
                }
                else
                {
                    CodingSequence = new Sequence(alphabet, sequence.ToString().Substring(utr5len, subEnd));
                }
            }
            return CodingSequence;
        }

        /// <summary>
        /// Get strands sorted by start (forward) or end and in reverse (reverse strand)
        /// </summary>
        /// <returns></returns>
        private List<Exon> SortedStrand()
        {
            return ExonsSortedStrand != null ?
                ExonsSortedStrand :
                IsStrandPlus() ?
                    Exons.OrderBy(x => x.OneBasedStart).ToList() :
                    Exons.OrderByDescending(x => x.OneBasedEnd).ToList();
        }

        public ISequence SplicedRNA()
        {
            bool ambiguity = ExonsSortedStrand.Any(x => x.Sequence.Alphabet.HasAmbiguity);
            return new Sequence(ambiguity ? Alphabets.AmbiguousDNA : Alphabets.DNA, String.Join("", ExonsSortedStrand.Select(x => SequenceExtensions.ConvertToString(x.Sequence))));
        }

        public Protein Protein()
        {
            return Protein(null);
        }

        public Protein Protein(Dictionary<string, string> selenocysteineContaining)
        {
            return Protein(RetrieveCodingSequence(), selenocysteineContaining);
        }

        private Protein Protein(ISequence dnaSeq, Dictionary<string, string> selenocysteineContaining)
        {
            selenocysteineContaining = selenocysteineContaining != null ? selenocysteineContaining : new Dictionary<string, string>();
            bool hasSelenocysteine = selenocysteineContaining.TryGetValue(ProteinID, out string selenocysteineContainingSeq);
            HashSet<int> uIndices = !hasSelenocysteine ? new HashSet<int>() : new HashSet<int>(Enumerable.Range(0, selenocysteineContainingSeq.Length).Where(i => selenocysteineContainingSeq[i] == 'U'));

            ISequence proteinSequence = Translation.OneFrameTranslation(dnaSeq, Gene.Chromosome.Mitochondrial);
            string proteinBases = !hasSelenocysteine ?
                SequenceExtensions.ConvertToString(proteinSequence) :
                // replace amber stop codons with selenocysteines where appropriate
                new string(Enumerable.Range(0, (int)proteinSequence.Count).Select(i => uIndices.Contains(i) && proteinSequence[i] == Alphabets.Protein.Ter ? (char)Alphabets.Protein.U : (char)proteinSequence[i]).ToArray());

            string proteinSequenceString = proteinBases.Split((char)Alphabets.Protein.Ter)[0];
            string annotations = String.Join(" ", VariantAnnotations);
            string accession = Translation.GetSafeProteinAccession(ProteinID);
            return new Protein(proteinSequenceString, accession, "Homo sapiens", null, null, null, annotations, annotations);
        }

        public IEnumerable<Protein> TranslateUsingAnnotatedStartCodons(Dictionary<Tuple<string, string, long>, List<CDS>> binnedCodingStarts, Dictionary<string, string> selenocysteineContaining, int indexBinSize, int minLength, bool includeVariants)
        {
            List<CDS> annotatedStarts = new List<CDS>();
            for (long i = Exons.Min(x => x.OneBasedStart) / indexBinSize; i < Exons.Max(x => x.OneBasedEnd) + 1; i++)
            {
                if (binnedCodingStarts.TryGetValue(new Tuple<string, string, long>(Gene.ChromosomeID, Strand, i * indexBinSize), out List<CDS> cds))
                {
                    annotatedStarts.AddRange(cds.Where(x =>
                        Exons.Any(xx => xx.Includes(IsStrandPlus() ? x.OneBasedStart : x.OneBasedEnd) // must include the start of the stop codon
                            && xx.Includes(IsStrandPlus() ? x.OneBasedStart + 2 : x.OneBasedEnd - 2)))); // and the end of the stop codon
                }
            }

            char terminatingCharacter = ProteinAlphabet.Instance.GetFriendlyName(Alphabets.Protein.Ter)[0];
            if (annotatedStarts.Count > 0)
            {
                foreach (CDS annotatedStart in annotatedStarts)
                {
                    long startCodonStart = IsStrandPlus() ? annotatedStart.OneBasedStart : annotatedStart.OneBasedEnd; // CDS on the reverse strand have start and end switched
                    ISequence cds;
                    if (IsStrandPlus())
                    {
                        long exonLengthBeforeCodingStart = Exons.Where(x => x.OneBasedEnd < annotatedStart.OneBasedStart).Sum(x => x.OneBasedEnd - x.OneBasedStart + 1);
                        long exonZeroBasedCodingStart = startCodonStart - Exons.FirstOrDefault(x => x.Includes(annotatedStart.OneBasedStart)).OneBasedStart;
                        long zeroBasedCodingStart = exonLengthBeforeCodingStart + exonZeroBasedCodingStart;
                        long lengthAfterCodingStart = Exons.Sum(x => x.Length()) - zeroBasedCodingStart;
                        cds = SplicedRNA().GetSubSequence(zeroBasedCodingStart, lengthAfterCodingStart);
                    }
                    else
                    {
                        long length = Exons.Sum(x => x.OneBasedEnd - x.OneBasedStart + 1);
                        long chop = Exons.Where(x => x.OneBasedEnd >= annotatedStart.OneBasedEnd).Sum(x => annotatedStart.OneBasedEnd < x.OneBasedStart ? x.OneBasedEnd - x.OneBasedStart + 1 : x.OneBasedEnd - annotatedStart.OneBasedEnd);
                        long lengthAfterCodingStart = length - chop;
                        cds = SplicedRNA().GetReversedSequence().GetSubSequence(0, lengthAfterCodingStart).GetReversedSequence();
                    }
                    Protein p = Protein(cds, selenocysteineContaining);
                    if (p.BaseSequence.Length >= minLength)
                    {
                        yield return p;
                    }
                }
            }
            //return Translation.ThreeFrameTranslation(Exons, ProteinID);
        }

        public bool isProteinCoding()
        {
            return CodingDomainSequences.Count > 0;
        }

        #endregion Translate from Variant Nucleotide Sequences Methods

        #region Create Interval Methods

        /// <summary>
        /// Create UTR regions for this transcript
        /// </summary>
        public List<UTR> CreateUTRs()
        {
            if (CodingDomainSequences.Count == 0)
            {
                return UTRs;
            }

            CodingDomainSequences = CodingDomainSequences.OrderBy(c => c.OneBasedStart).ToList();
            long codingLeft = CodingDomainSequences.First().OneBasedStart;
            long codingRight = CodingDomainSequences.Last().OneBasedEnd;
            foreach (Exon x in Exons.OrderBy(c => c.OneBasedStart))
            {
                if (x.OneBasedStart < codingLeft)
                {
                    long end = x.OneBasedEnd < codingLeft ? x.OneBasedEnd : codingLeft;
                    UTRs.Add(IsStrandPlus() ? new UTR5Prime(x, ChromosomeID, Strand, x.OneBasedStart, end) as UTR : new UTR3Prime(x, ChromosomeID, Strand, x.OneBasedStart, end) as UTR);
                }
                if (x.OneBasedEnd > CodingDomainSequences.Last().OneBasedEnd)
                {
                    long start = x.OneBasedStart < codingRight ? x.OneBasedStart : codingRight;
                    UTRs.Add(IsStrandPlus() ? new UTR3Prime(x, ChromosomeID, Strand, start, x.OneBasedEnd) as UTR : new UTR5Prime(x, ChromosomeID, Strand, start, x.OneBasedEnd) as UTR);
                }
            }
            return UTRs;
        }

        /// <summary>
        /// Creates upstream and downstream intervals for this transcript
        /// </summary>
        /// <param name="chromosomeSequence"></param>
        public List<Interval> CreateUpDown(Chromosome chromosomeSequence)
        {
            long chrMin = 1;
            long chrMax = chromosomeSequence.Sequence.Count;

            // Create up/down stream intervals and add them to the list
            long beforeStart = Math.Max(chrMin - DEFAULT_UP_DOWN_LENGTH, chrMin);
            long beforeEnd = Math.Max(chrMin - 1, chrMin);
            long afterStart = Math.Min(OneBasedEnd + 1, chrMax);
            long afterEnd = Math.Min(OneBasedEnd + DEFAULT_UP_DOWN_LENGTH, chrMax);

            if (IsStrandPlus())
            {
                if (beforeStart < beforeEnd) { Upstream = new Upstream(this, chromosomeSequence.ChromosomeID, Strand, beforeStart, beforeEnd); }
                if (afterStart < afterEnd) { Downstream = new Downstream(this, chromosomeSequence.ChromosomeID, Strand, afterStart, afterEnd); }
            }
            else
            {
                if (afterStart < afterEnd) { Upstream = new Upstream(this, chromosomeSequence.ChromosomeID, Strand, afterStart, afterEnd); }
                if (beforeStart < beforeEnd) { Downstream = new Downstream(this, chromosomeSequence.ChromosomeID, Strand, beforeStart, beforeEnd); }
            }

            return new List<Interval> { Upstream, Downstream };
        }

        public List<Intron> CreateIntrons()
        {
            Exon previous = null;
            foreach (Exon x in Exons)
            {
                if (previous == null)
                {
                    previous = x;
                    continue;
                }
                Intron intron = new Intron(this, x.ChromosomeID, x.Strand, previous.OneBasedEnd + 1, x.OneBasedStart - 1);
                if (intron.Length() > 0)
                {
                    Introns.Add(intron);
                }
            }
            return Introns;
        }

        #endregion Create Interval Methods

        #region Warning Methods

        /// <summary>
        /// Perfom some baseic chekcs, return error type, if any
        /// </summary>
        /// <param name="variant"></param>
        /// <returns></returns>
        public ErrorWarningType sanityCheck(Variant variant)
        {
            if (isErrorStopCodonsInCds()) { return ErrorWarningType.WARNING_TRANSCRIPT_MULTIPLE_STOP_CODONS; }
            if (isErrorProteinLength()) { return ErrorWarningType.WARNING_TRANSCRIPT_INCOMPLETE; }
            if (isErrorStartCodon()) { return ErrorWarningType.WARNING_TRANSCRIPT_NO_START_CODON; }
            if (isWarningStopCodon()) { return ErrorWarningType.WARNING_TRANSCRIPT_NO_STOP_CODON; }
            return ErrorWarningType.NONE;
        }

        /// <summary>
        /// Check if coding length is multiple of 3 in protein coding transcripts
        /// </summary>
        /// <returns></returns>
        public bool isErrorProteinLength()
        {
            if (!isProteinCoding()) return false; //!Config.get().isTreatAllAsProteinCoding() &&
            return (RetrieveCodingSequence().Count % 3) != 0;
        }

        /// <summary>
        /// Is the first codon a START codon?
        /// </summary>
        /// <returns></returns>
        public bool isErrorStartCodon()
        {
            if (
                //!Config.get().isTreatAllAsProteinCoding() &&
                !isProteinCoding())
            {
                return false;
            }

            // Not even one codon in this protein? Error
            ISequence cds = RetrieveCodingSequence();
            if (cds.Count < 3) { return true; }

            string codon = SequenceExtensions.ConvertToString(cds.GetSubSequence(0, 3)).ToUpper(CultureInfo.InvariantCulture);
            return !(Gene.Chromosome.Mitochondrial ? CodonsVertebrateMitochondrial.START_CODONS.Contains(codon) : CodonsStandard.START_CODONS.Contains(codon));
        }

        /// <summary>
        /// Check if protein sequence has STOP codons in the middle of the coding sequence
        /// </summary>
        /// <returns></returns>
        public bool isErrorStopCodonsInCds()
        {
            //if (!Config.get().isTreatAllAsProteinCoding() && !isProteinCoding()) return false;

            // Get protein sequence
            string prot = Protein().BaseSequence;
            if (prot == null) { return false; }

            // Any STOP codon before the end?
            char[] bases = prot.ToCharArray();
            int max = bases.Length - 1;
            int countErrs = 0;
            for (int i = 0; i < max; i++)
            {
                if (bases[i] == '*')
                {
                    countErrs++;
                    // We allow up to one STOP codon because it can be a RARE_AMINO_ACID which is coded as a STOP codon.
                    // More than one STOP codon is not "normal", so it's probably an error in the genomic annotations (e.g. ENSEMBL or UCSC)
                    if (countErrs > 1) { return true; }
                }
            }

            // OK
            return false;
        }

        /// <summary>
        /// Is the last codon a STOP codon?
        /// </summary>
        /// <returns></returns>
        public bool isWarningStopCodon()
        {
            if (isProteinCoding()) { return false; }//!Config.get().isTreatAllAsProteinCoding() &&

            // Not even one codon in this protein? Error
            ISequence cds = RetrieveCodingSequence();
            if (cds.Count < 3) { return true; }

            ISequence codon = cds.GetSubSequence(cds.Count - CodonChange.CODON_SIZE, CodonChange.CODON_SIZE);
            return Codons.TryLookup(codon[0], codon[1], codon[2], out byte aa) && aa == Alphabets.Protein.Ter;
        }

        #endregion Warning Methods
    }
}