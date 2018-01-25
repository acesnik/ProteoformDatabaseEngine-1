﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ToolWrapperLayer;

namespace WorkflowLayer
{
    /// <summary>
    /// Workflow for installing and managing packages and programs.
    /// </summary>
    public class ManagePackagesFlow
    {

        #region Private Field

        /// <summary>
        /// List of dependencies to fetch from aptitude using `sudo apt-get install`
        /// </summary>
        private static List<string> aptitudeDependencies = new List<string>
        {
            // installers
            "gcc",
            "g++",
            "gfortran",
            "make",
            "cmake",
            "build-essential",

            // file compression
            "zlib1g-dev",
            "unzip",
            "liblzma-dev",
            "libncurses5-dev",
            "libbz2-dev",


            // bioinformatics -- keep this to illustrate that these things are super outdated in aptitude
            //"samtools", // out of date
            //"tophat", // super outdated in aptitude
            //"cufflinks", // super outdated in aptitude
            //"bedtools", // super outdated (2013) in aptitude
            //"ncbi-blast+", // very outdated (2.2 from 2014, instead of 2.7)
            //"melting", // outdated (v4 in aptidude, v5 on main site which was totally rewritten)

            // commandline tools
            "gawk",
            "git",
            "python",
            "python-dev",
            "python-setuptools",
            "libpython2.7-dev",
        };

        /// <summary>
        /// List of tools for installation or removal.
        /// </summary>
        private static List<IInstallable> tools = new List<IInstallable>
        {
            // require root permissions
            new BEDOPSWrapper(),
            new BLASTWrapper(),
            new LastzWrapper(),
            new MeltingWrapper(),
            new MfoldWrapper(),
            new SamtoolsWrapper(),

            // don't necessarily require root permissions
            new BedtoolsWrapper(),
            new CufflinksWrapper(),
            new GATKWrapper(),
            new HISAT2Wrapper(),
            new RSeQCWrapper(),
            new ScalpelWrapper(),
            new SkewerWrapper(),
            new SlnckyWrapper(),
            new SnpEffWrapper(),
            new SRAToolkitWrapper(),
            new STARWrapper(),
            new STARFusionWrapper(),
            new TopHatWrapper()
        };

        #endregion Private Field

        #region Public Methods

        /// <summary>
        /// Installs packages and programs for analysis in Spritz.
        /// </summary>
        /// <param name="binDirectory"></param>
        public static void Install(string binDirectory)
        {
            // get root permissions and update and upgrade the repositories
            List<string> commands = new List<string> 
            {
                "echo \"Checking for updates and installing any missing dependencies. Please enter your password for this step:\n\"",
                "sudo apt-get -y update",
                "sudo apt-get -y upgrade",
            };

            // install dependencies from aptitude
            foreach (string dependency in aptitudeDependencies)
            {
                commands.Add
                (
                    "if commmand -v " + dependency + " > /dev/null 2>&1 ; then\n" +
                    "  echo found\n" +
                    "else\n" +
                    "  sudo apt-get -y install " + dependency + "\n" +
                    "fi"
                );
            }

            // python setup
            commands.Add("sudo easy_install pip");
            commands.Add("sudo pip install --upgrade virtualenv");
            commands.Add("pip install --upgrade pip");
            commands.Add("sudo pip install --upgrade qc bitsets cython bx-python pysam RSeQC numpy"); // for RSeQC

            // java8 setup
            commands.Add
            (
                "version=$(java -version 2>&1 | awk -F '\"' '/version/ {print $2}')\n" +
                "if [[ \"$version\" > \"1.5\" ]]; then\n" +
                "  echo found\n" +
                "else\n" +
                "  sudo add-apt-repository ppa:webupd8team/java\n" +
                "  sudo apt-get -y update\n" +
                "  sudo apt-get -y install openjdk-8-jdk\n" + // need the JDK for some GATK install, not the JRE found in oracle-java8-installer
                "fi"
            );

            // write some scripts in parallel with root permissions
            string installationLogsDirectory = Path.Combine(binDirectory, "scripts", "installLogs");
            Directory.CreateDirectory(installationLogsDirectory);
            List<string> parallelScripts = tools.Select(t => t.WriteInstallScript(binDirectory)).ToList();

            // run scripts in background
            for (int i = 0; i < parallelScripts.Count; i++)
            {
                commands.Add("echo \"Running " + parallelScripts[i] + " in the background.\"");
                commands.Add("bash " + WrapperUtility.ConvertWindowsPath(parallelScripts[i]) + " &> " + 
                    WrapperUtility.ConvertWindowsPath(Path.Combine(installationLogsDirectory, Path.GetFileNameWithoutExtension(parallelScripts[i]) + ".log")) + 
                    " &");
                commands.Add("proc" + i.ToString() + "=$!");
            }

            // wait on the scripts to finish
            for (int i = 0; i < parallelScripts.Count; i++)
            {
                commands.Add("wait $proc" + i.ToString());
            }

            // write the and run the installations requiring root permissions
            string scriptPath = Path.Combine(binDirectory, "scripts", "installScripts", "installDependencies.bash");
            WrapperUtility.GenerateAndRunScript(scriptPath, commands).WaitForExit();
        }

        /// <summary>
        /// Cleans all programs installed by Spritz. Intentionally leaves packages installed from aptitude, since they're effectively only base packages, now.
        /// </summary>
        /// <param name="binDirectory"></param>
        public static void Clean(string binDirectory)
        {
            List<string> commands = new List<string>();

            // write some scripts in parallel with root permissions
            string toolRemovalLogs = Path.Combine(binDirectory, "scripts", "toolRemovalLogs");
            Directory.CreateDirectory(toolRemovalLogs);
            List<string> parallelScripts = tools.Select(t => t.WriteRemoveScript(binDirectory)).ToList();

            // run scripts in background
            for (int i = 0; i < parallelScripts.Count; i++)
            {
                commands.Add("echo \"Running " + parallelScripts[i] + " in the background.\"");
                commands.Add("bash " + WrapperUtility.ConvertWindowsPath(parallelScripts[i]) + " &> " +
                    WrapperUtility.ConvertWindowsPath(Path.Combine(toolRemovalLogs, Path.GetFileNameWithoutExtension(parallelScripts[i]) + ".log")) +
                    " &");
                commands.Add("proc" + i.ToString() + "=$!");
            }

            // wait on the scripts to finish
            for (int i = 0; i < parallelScripts.Count; i++)
            {
                commands.Add("wait $proc" + i.ToString());
            }

            // write the and run the installations requiring root permissions
            string scriptPath = Path.Combine(binDirectory, "scripts", "removalScripts", "installDependencies.bash");
            WrapperUtility.GenerateAndRunScript(scriptPath, commands).WaitForExit();
        }

        #endregion Public Methods

    }
}
