﻿using System.Collections.Generic;
using System.IO;

namespace ToolWrapperLayer
{
    public class ScalpelWrapper
    {
        // Need to filter VCF by FILTER = PASS; there are several reasons they don't accept calls that I trust
        // There's an attribute "ZYG" for zygosity, either "het" or "homo" for heterozygous or homozygous
        public static void call_indels(string bin_directory, int threads, string genome_fasta, string bed, string bam, string outdir, out string new_vcf)
        {
            new_vcf = Path.Combine(outdir, "variants.indel.vcf");
            string script_path = Path.Combine(bin_directory, "scripts", "scalpel.bash");
            WrapperUtility.GenerateAndRunScript(script_path, new List<string>
            {
                "cd " + WrapperUtility.ConvertWindowsPath(bin_directory),
                "scalpel-0.5.3/scalpel-discovery --single " +
                    "--bam " + WrapperUtility.ConvertWindowsPath(bam) +
                    " --ref " + WrapperUtility.ConvertWindowsPath(genome_fasta) +
                    " --bed " + WrapperUtility.ConvertWindowsPath(bed) +
                    " --numprocs " + threads.ToString() + 
                    " --dir " + WrapperUtility.ConvertWindowsPath(outdir),
            }).WaitForExit();
        }

        // Requires cmake, installed in WrapperUtility install
        public static void Install(string current_directory)
        {
            if (Directory.Exists(Path.Combine(current_directory, "scalpel-0.5.3"))) return;
            string script_path = Path.Combine(current_directory, "scripts", "install_scalpel.bash");
            WrapperUtility.GenerateAndRunScript(script_path, new List<string>
            {
                "cd " + WrapperUtility.ConvertWindowsPath(current_directory),
                @"wget --no-check http://sourceforge.net/projects/scalpel/files/scalpel-0.5.3.tar.gz; tar zxvf scalpel-0.5.3.tar.gz; cd scalpel-0.5.3; make",
                "cd ..",
                "rm scalpel-0.5.3.tar.gz",
            }).WaitForExit();
        }
    }
}
