﻿namespace Proteogenomics
{
    public class CDS :
        Interval
    {
        #region Constructors

        public CDS(string chromID, string strand, long oneBasedStart, long oneBasedEnd)
            : base(chromID, strand, oneBasedStart, oneBasedEnd)
        {
        }

        public CDS(CDS cds)
            : base(cds)
        {
        }

        #endregion Constructors
    }
}