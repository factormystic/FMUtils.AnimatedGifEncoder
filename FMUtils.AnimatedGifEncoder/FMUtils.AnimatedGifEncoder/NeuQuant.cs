using System;

namespace FMUtils.AnimatedGifEncoder
{
    // NeuQuant Neural-Net Quantization Algorithm
    // ------------------------------------------
    // 
    // Copyright (c) 1994 Anthony Dekker
    // 
    // NEUQUANT Neural-Net quantization algorithm by Anthony Dekker, 1994. See
    // "Kohonen neural networks for optimal colour quantization" in "Network:
    // Computation in Neural Systems" Vol. 5 (1994) pp 351-367. for a discussion of
    // the algorithm.
    // 
    // Any party obtaining a copy of these files from the author, directly or
    // indirectly, is granted, free of charge, a full and unrestricted irrevocable,
    // world-wide, paid up, royalty-free, nonexclusive right and license to deal in
    // this software and documentation files (the "Software"), including without
    // limitation the rights to use, copy, modify, merge, publish, distribute,
    // sublicense, and/or sell copies of the Software, and to permit persons who
    // receive copies from any such party to do so, with the only requirement being
    // that this copyright notice remain intact.
    //
    // Ported to Java 12/00 K Weiner
    // Originated from: http://www.java2s.com/Code/Java/2D-Graphics-GUI/AnimatedGifEncoder.htm

    /*
     * Program Skeleton
     * [select samplefac in range 1..30]
     * [read image from input file]
     * pic = (unsigned char*)
     * malloc(3*width*height);
     * initnet(pic, 3*width*height, samplefac);
     * learn();
     * unbiasnet();
     * [write output image header, using writecolourmap(f)]
     * inxbuild();
     * write output image using inxsearch(b,g,r)
    */

    internal class NeuQuant
    {
        /// <summary>
        /// number of colours used
        /// </summary>
        int netsize = 256;

        /* four primes near 500 - assume no image has a length so large that it is divisible by all four primes */
        static int prime1 = 499;
        static int prime2 = 491;
        static int prime3 = 487;
        static int prime4 = 503;

        /// <summary>
        /// minimum size for input image
        /// </summary>
        static int minpicturebytes = (3 * prime4);

        /*
         * Network Definitions -------------------
         */

        int maxnetpos;

        /// <summary>
        /// bias for colour values
        /// </summary>
        static int netbiasshift = 4;

        /// <summary>
        /// no. of learning cycles
        /// </summary>
        static int ncycles = 100;

        /// <summary>
        /// bias for fractions
        /// </summary>
        static int intbiasshift = 16;

        static int intbias = (((int)1) << intbiasshift);

        /// <summary>
        /// gamma = 1024
        /// </summary>
        static int gammashift = 10;

        static int gamma = (((int)1) << gammashift);

        static int betashift = 10;

        /// <summary>
        /// beta = 1/1024
        /// </summary>
        static int beta = (intbias >> betashift);

        static int betagamma = (intbias << (gammashift - betashift));

        /* defs for decreasing radius factor */
        /* for 256 cols, radius starts at 32.0 biased by 6 bits and decreases by a factor of 1/30 each cycle */
        int initrad;
        int radiusbiasshift = 6;
        int radiusbias;
        int initradius;
        static int radiusdec = 30;

        /// <summary>
        /// alpha starts at 1.0
        /// </summary>
        static int alphabiasshift = 10;
        static int initalpha = (((int)1) << alphabiasshift);

        /// <summary>
        /// biased by 10 bits
        /// </summary>
        //int alphadec;

        /// <summary>
        /// radbias and alpharadbias used for radpower calculation
        /// </summary>
        static int radbiasshift = 8;

        static int radbias = (((int)1) << radbiasshift);

        static int alpharadbshift = (alphabiasshift + radbiasshift);

        static int alpharadbias = (((int)1) << alpharadbshift);

        /*
         * Types and Global Variables --------------------------
         */

        /// <summary>
        /// the input image itself
        /// </summary>
        byte[] thepicture;

        /// <summary>
        /// lengthcount = H*W*3
        /// </summary>
        //int lengthcount;

        /// <summary>
        /// sampling factor 1..30
        /// </summary>
        int samplefac;

        // typedef int pixel[4]; /* BGRc */

        /// <summary>
        /// the network itself - [netsize][4]
        /// </summary>
        int[][] network;

        int[] netindex;

        /// <summary>
        /// for network lookup - really 256
        /// </summary>
        int[] bias;

        /// <summary>
        /// bias and freq arrays for learning
        /// </summary>
        int[] freq;

        /// <summary>
        /// radpower for precomputation
        /// </summary>
        int[] radpower;


        /// <summary>
        /// Initialise network in range (0,0,0) to (255,255,255) and set parameters
        /// </summary>
        public NeuQuant(byte[] thepic, int max_colors, int sample)
        {
            int i;
            int[] p;

            this.thepicture = thepic;
            //this.lengthcount = thepic.Length;

            this.netsize = max_colors;
            this.samplefac = this.thepicture.Length < NeuQuant.minpicturebytes ? 1 : sample;
            this.maxnetpos = netsize - 1;
            this.initrad = (netsize >> 3);
            this.radiusbias = (((int)1) << radiusbiasshift);
            this.initradius = (initrad * radiusbias);

            this.netindex = new int[netsize];
            this.bias = new int[netsize];
            this.freq = new int[netsize];
            this.radpower = new int[initrad];

            this.network = new int[netsize][];
            for (i = 0; i < this.netsize; i++)
            {
                this.network[i] = new int[4];
                p = this.network[i];
                p[0] = p[1] = p[2] = (i << (NeuQuant.netbiasshift + 8)) / this.netsize;
                this.freq[i] = NeuQuant.intbias / this.netsize; /* 1/netsize */
                this.bias[i] = 0;
            }
        }

        public int[][] Process()
        {
            Learn();
            UnbiasNetwork();
            BuildIndex();
            return this.network;
        }

        /// <summary>
        /// Search for BGR values 0..255 (after net is unbiased) and return colour index
        /// </summary>
        public int Map(int b, int g, int r)
        {
            int dist, a, bestd;
            int[] p;
            int best;

            /* biggest possible dist is netsize*3 */
            bestd = 1000;
            best = -1;

            /* index on g */
            int i = netindex[Math.Min(g, netsize - 1)];

            /* start at netindex[g] and work outwards */
            int j = i - 1;

            while ((i < netsize) || (j >= 0))
            {
                if (i < netsize)
                {
                    p = network[i];

                    /* inx key */
                    dist = p[1] - g;

                    if (dist >= bestd)
                    {
                        /* stop iter */
                        i = netsize;
                    }
                    else
                    {
                        i++;

                        if (dist < 0)
                            dist = -dist;

                        a = p[0] - b;

                        if (a < 0)
                            a = -a;

                        dist += a;

                        if (dist < bestd)
                        {
                            a = p[2] - r;

                            if (a < 0)
                                a = -a;

                            dist += a;

                            if (dist < bestd)
                            {
                                bestd = dist;
                                best = p[3];
                            }
                        }
                    }
                }

                if (j >= 0)
                {
                    p = network[j];

                    /* inx key - reverse dif */
                    dist = g - p[1];

                    if (dist >= bestd)
                    {
                        /* stop iter */
                        j = -1;
                    }
                    else
                    {
                        j--;
                        if (dist < 0)
                            dist = -dist;

                        a = p[0] - b;
                        if (a < 0)
                            a = -a;

                        dist += a;

                        if (dist < bestd)
                        {
                            a = p[2] - r;
                            if (a < 0)
                                a = -a;

                            dist += a;

                            if (dist < bestd)
                            {
                                bestd = dist;
                                best = p[3];
                            }
                        }
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Main Learning Loop
        /// </summary>
        void Learn()
        {
            int step;

            int alphadec = 30 + ((samplefac - 1) / 3);
            int pix = 0;
            int samplepixels = this.thepicture.Length / (3 * samplefac);
            int alpha = NeuQuant.initalpha;
            int radius = initradius;

            int rad = radius >> radiusbiasshift;
            if (rad <= 1)
                rad = 0;

            for (int n = 0; n < rad; n++)
            {
                radpower[n] = alpha * (((rad * rad - n * n) * radbias) / (rad * rad));
            }

            if (this.thepicture.Length < minpicturebytes)
            {
                step = 3;
            }
            else if ((this.thepicture.Length % prime1) != 0)
            {
                step = 3 * prime1;
            }
            else
            {
                if ((this.thepicture.Length % prime2) != 0)
                {
                    step = 3 * prime2;
                }
                else
                {
                    if ((this.thepicture.Length % prime3) != 0)
                    {
                        step = 3 * prime3;
                    }
                    else
                    {
                        step = 3 * prime4;
                    }
                }
            }

            int delta = samplepixels / ncycles;
            if (delta == 0)
                delta = 1;

            int i = 0;
            while (i < samplepixels)
            {
                int b = (this.thepicture[pix + 0] & 0xff) << NeuQuant.netbiasshift;
                int g = (this.thepicture[pix + 1] & 0xff) << NeuQuant.netbiasshift;
                int r = (this.thepicture[pix + 2] & 0xff) << NeuQuant.netbiasshift;
                int j = Contest(b, g, r);

                AlterSingle(alpha, j, b, g, r);

                /* alter neighbours */
                if (rad != 0)
                    AlterNeighbor(rad, j, b, g, r);

                pix += step;

                if (pix >= this.thepicture.Length)
                    pix -= this.thepicture.Length;

                i++;

                if (i % delta == 0)
                {
                    alpha -= alpha / alphadec;
                    radius -= radius / NeuQuant.radiusdec;
                    rad = radius >> radiusbiasshift;
                    if (rad <= 1)
                        rad = 0;
                    for (j = 0; j < rad; j++)
                        radpower[j] = alpha * (((rad * rad - j * j) * radbias) / (rad * rad));
                }
            }
        }

        /// <summary>
        /// Unbias network to give byte values 0..255 and record position i to prepare for sort
        /// </summary>
        void UnbiasNetwork()
        {
            for (int i = 0; i < netsize; i++)
            {
                network[i][0] >>= NeuQuant.netbiasshift;
                network[i][1] >>= NeuQuant.netbiasshift;
                network[i][2] >>= NeuQuant.netbiasshift;
                network[i][3] = i; /* record colour no */
            }
        }

        /// <summary>
        /// Insertion sort of network and building of netindex[0..255] (to do after unbias)
        /// </summary>
        void BuildIndex()
        {
            int i, j, smallpos, smallval;
            int[] p;
            int[] q;
            int previouscol, startpos;

            previouscol = 0;
            startpos = 0;

            for (i = 0; i < this.netsize; i++)
            {
                p = this.network[i];
                smallpos = i;
                smallval = p[1]; /* index on g */

                /* find smallest in i..netsize-1 */
                for (j = i + 1; j < this.netsize; j++)
                {
                    q = this.network[j];
                    if (q[1] < smallval)
                    {
                        /* index on g */
                        smallpos = j;
                        smallval = q[1];
                    }
                }

                q = this.network[smallpos];

                /* swap p (i) and q (smallpos) entries */
                if (i != smallpos)
                {
                    j = q[0];
                    q[0] = p[0];
                    p[0] = j;
                    j = q[1];
                    q[1] = p[1];
                    p[1] = j;
                    j = q[2];
                    q[2] = p[2];
                    p[2] = j;
                    j = q[3];
                    q[3] = p[3];
                    p[3] = j;
                }

                /* smallval entry is now in position i */
                if (smallval != previouscol)
                {
                    this.netindex[previouscol] = (startpos + i) >> 1;
                    for (j = previouscol + 1; j < smallval; j++)
                        this.netindex[j] = i;
                    previouscol = smallval;
                    if (previouscol >= netsize)
                        previouscol = this.netsize - 1;
                    startpos = i;
                }
            }

            this.netindex[previouscol] = (startpos + this.maxnetpos) >> 1;

            for (j = previouscol + 1; j < this.netsize; j++)
                this.netindex[j] = this.maxnetpos; /* really netsize */
        }


        /// <summary>
        /// Move adjacent neurons by precomputed alpha*(1-((i-j)^2/[r]^2)) in radpower[|i-j|]
        /// </summary>
        void AlterNeighbor(int rad, int i, int b, int g, int r)
        {
            int j, k, lo, hi, a, m;
            int[] p;

            lo = i - rad;
            if (lo < -1)
                lo = -1;

            hi = i + rad;
            if (hi > netsize)
                hi = netsize;

            j = i + 1;
            k = i - 1;
            m = 1;

            while ((j < hi) || (k > lo))
            {
                a = radpower[m++];

                if (j < hi)
                {
                    p = network[j++];

                    try
                    {
                        p[0] -= (a * (p[0] - b)) / alpharadbias;
                        p[1] -= (a * (p[1] - g)) / alpharadbias;
                        p[2] -= (a * (p[2] - r)) / alpharadbias;
                    }
                    catch (Exception e)
                    {
                        // prevents 1.3 miscompilation
                    }
                }

                if (k > lo)
                {
                    p = network[k--];

                    try
                    {
                        p[0] -= (a * (p[0] - b)) / alpharadbias;
                        p[1] -= (a * (p[1] - g)) / alpharadbias;
                        p[2] -= (a * (p[2] - r)) / alpharadbias;
                    }
                    catch (Exception e)
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Move neuron i towards biased (b,g,r) by factor alpha
        /// </summary>
        void AlterSingle(int alpha, int i, int b, int g, int r)
        {
            network[i][0] -= (alpha * (network[i][0] - b)) / initalpha;
            network[i][1] -= (alpha * (network[i][1] - g)) / initalpha;
            network[i][2] -= (alpha * (network[i][2] - r)) / initalpha;
        }

        /// <summary>
        /// Search for biased BGR values
        /// </summary>
        int Contest(int b, int g, int r)
        {
            /* finds closest neuron (min dist) and updates freq */
            /* finds best neuron (min dist-bias) and returns position */
            /* for frequently chosen neurons, freq[i] is high and bias[i] is negative */
            /* bias[i] = gamma*((1/netsize)-freq[i]) */

            int dist, a, biasdist, betafreq;
            int[] n;

            int bestd = ~(((int)1) << 31);
            int bestbiasd = bestd;
            int bestpos = -1;
            int bestbiaspos = bestpos;

            for (int i = 0; i < netsize; i++)
            {
                n = network[i];

                dist = n[0] - b;
                if (dist < 0)
                    dist = -dist;

                a = n[1] - g;
                if (a < 0)
                    a = -a;

                dist += a;

                a = n[2] - r;
                if (a < 0)
                    a = -a;

                dist += a;

                if (dist < bestd)
                {
                    bestd = dist;
                    bestpos = i;
                }

                biasdist = dist - ((bias[i]) >> (intbiasshift - NeuQuant.netbiasshift));

                if (biasdist < bestbiasd)
                {
                    bestbiasd = biasdist;
                    bestbiaspos = i;
                }

                betafreq = (freq[i] >> betashift);
                freq[i] -= betafreq;
                bias[i] += (betafreq << gammashift);
            }

            freq[bestpos] += beta;
            bias[bestpos] -= betagamma;

            return bestbiaspos;
        }
    }
}
