﻿namespace Proteogenomics
{
    public abstract class UTR :
        Interval
    {
        protected UTR(Exon parent, string chromID, string strand, long oneBasedStart, long oneBasedEnd)
            : base(parent, chromID, strand, oneBasedStart, oneBasedEnd)
        {
        }

        protected UTR(UTR utr)
            : base(utr)
        {
        }

        public abstract bool is3Prime();

        public abstract bool is5Prime();

        public abstract override bool VariantEffect(Variant variant, VariantEffects variantEffects);
    }
}