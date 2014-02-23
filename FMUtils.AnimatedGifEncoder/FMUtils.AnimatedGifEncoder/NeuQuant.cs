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
        /// radbias and alpharadbias used for radpower calculation
        /// </summary>
        static int radbiasshift = 8;

        static int radbias = (((int)1) << radbiasshift);

        static int alpharadbshift = (alphabiasshift + radbiasshift);

        static int alpharadbias = (((int)1) << alpharadbshift);

        /// <summary>
        /// the input image itself
        /// </summary>
        byte[] thepicture;

        /// <summary>
        /// sampling factor 1..30
        /// </summary>
        int samplefac;

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
            this.thepicture = thepic;

            this.netsize = Math.Min(max_colors, this.netsize);
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
            for (int i = 0; i < this.netsize; i++)
            {
                this.network[i] = new int[4];
                this.network[i][0] = this.network[i][1] = this.network[i][2] = (i << (NeuQuant.netbiasshift + 8)) / this.netsize;
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
            int dist, a;

            /* biggest possible dist is netsize*3 */
            int bestd = 1000;
            int best = -1;

            /* index on g */
            int i = netindex[Math.Min(g, netsize - 1)];

            /* start at netindex[g] and work outwards */
            int j = i - 1;

            while ((i < netsize) || (j >= 0))
            {
                if (i < netsize)
                {
                    /* inx key */
                    dist = this.network[i][1] - g;

                    if (dist >= bestd)
                    {
                        /* stop iter */
                        i = this.netsize;
                    }
                    else
                    {
                        if (dist < 0)
                            dist = -dist;

                        a = this.network[i][0] - b;
                        if (a < 0)
                            a = -a;

                        dist += a;

                        if (dist < bestd)
                        {
                            a = this.network[i][2] - r;
                            if (a < 0)
                                a = -a;

                            dist += a;

                            if (dist < bestd)
                            {
                                bestd = dist;
                                best = this.network[i][3];
                            }
                        }

                        i++;
                    }
                }

                if (j >= 0)
                {
                    /* inx key - reverse dif */
                    dist = g - this.network[j][1];

                    if (dist >= bestd)
                    {
                        /* stop iter */
                        j = -1;
                    }
                    else
                    {
                        if (dist < 0)
                            dist = -dist;

                        a = this.network[j][0] - b;
                        if (a < 0)
                            a = -a;

<<<<<<< HEAD
                        dist += a;
=======
        //public byte[] colorMap()
        public int[][] colorMap()
        {
            //byte[] map = new byte[3 * netsize];
            //int[] index = new int[netsize];
            //for (int i = 0; i < netsize; i++)
            //    index[network[i][3]] = i;
            //int k = 0;
            //for (int i = 0; i < netsize; i++)
            //{
            //    int j = index[i];
            //    map[k++] = (byte)(network[j][0]);
            //    map[k++] = (byte)(network[j][1]);
            //    map[k++] = (byte)(network[j][2]);
            //}
            //return map;


            //byte[] map = new byte[3 * netsize];

            //for (int i = 0; i < netsize; i += 3)
            //{
            //    map[i] = (byte)network[i / 3][0];
            //    map[i + 1] = (byte)network[i / 3][1];
            //    map[i + 2] = (byte)network[i / 3][2];
            //}

            //return map;
>>>>>>> work in progress

                        if (dist < bestd)
                        {
                            a = this.network[j][2] - r;
                            if (a < 0)
                                a = -a;

                            dist += a;

                            if (dist < bestd)
                            {
                                bestd = dist;
                                best = this.network[j][3];
                            }
                        }

                        j--;
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

            int alphadec = 30 + ((this.samplefac - 1) / 3);
            int pix = 0;
            int samplepixels = this.thepicture.Length / (3 * this.samplefac);
            int alpha = NeuQuant.initalpha;
            int radius = this.initradius;

            int rad = radius >> this.radiusbiasshift;
            if (rad <= 1)
                rad = 0;

            for (int n = 0; n < rad; n++)
            {
                this.radpower[n] = alpha * (((rad * rad - n * n) * NeuQuant.radbias) / (rad * rad));
            }

            if (this.thepicture.Length < NeuQuant.minpicturebytes)
            {
                step = 3;
            }
            else if ((this.thepicture.Length % NeuQuant.prime1) != 0)
            {
                step = 3 * NeuQuant.prime1;
            }
            else
            {
                if ((this.thepicture.Length % NeuQuant.prime2) != 0)
                {
                    step = 3 * NeuQuant.prime2;
                }
                else
                {
                    if ((this.thepicture.Length % NeuQuant.prime3) != 0)
                    {
                        step = 3 * NeuQuant.prime3;
                    }
                    else
                    {
                        step = 3 * NeuQuant.prime4;
                    }
                }
            }

            int delta = samplepixels / NeuQuant.ncycles;
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
            for (int i = 0; i < this.netsize; i++)
            {
                this.network[i][0] >>= NeuQuant.netbiasshift;
                this.network[i][1] >>= NeuQuant.netbiasshift;
                this.network[i][2] >>= NeuQuant.netbiasshift;
                this.network[i][3] = i; /* record colour no */
            }
        }

        /// <summary>
        /// Insertion sort of network and building of netindex[0..255] (to do after unbias)
        /// </summary>
        void BuildIndex()
        {
            int j;
            int previouscol = 0;
            int startpos = 0;

            for (int i = 0; i < this.netsize; i++)
            {
                /* index on g */
                int smallpos = i;
                int smallval = this.network[i][1];

                /* find smallest in i..netsize-1 */
                for (j = i + 1; j < this.netsize; j++)
                {
                    if (this.network[j][1] < smallval)
                    {
                        /* index on g */
                        smallpos = j;
                        smallval = this.network[j][1];
                    }
                }

                /* swap i and smallpos entries */
                if (i != smallpos)
                {
                    j = this.network[smallpos][0];
                    this.network[smallpos][0] = this.network[i][0];
                    this.network[i][0] = j;

                    j = this.network[smallpos][1];
                    this.network[smallpos][1] = this.network[i][1];
                    this.network[i][1] = j;

                    j = this.network[smallpos][2];
                    this.network[smallpos][2] = this.network[i][2];
                    this.network[i][2] = j;

                    j = this.network[smallpos][3];
                    this.network[smallpos][3] = this.network[i][3];
                    this.network[i][3] = j;
                }

<<<<<<< HEAD
                /* smallval entry is now in position i */
                if (smallval != previouscol)
                {
                    this.netindex[previouscol] = (startpos + i) >> 1;
=======
        //public byte[] process()
        public int[][] process()
        {
            learn();
            unbiasnet();
            inxbuild();
            return colorMap();
        }
>>>>>>> work in progress

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
            int lo = Math.Max(i - rad, -1);
            int hi = Math.Min(i + rad, this.netsize);

            int j = i + 1;
            int k = i - 1;
            int m = 1;

            while ((j < hi) || (k > lo))
            {
                if (j < hi)
                {
                    try
                    {
                        this.network[j][0] -= (this.radpower[m] * (this.network[j][0] - b)) / NeuQuant.alpharadbias;
                        this.network[j][1] -= (this.radpower[m] * (this.network[j][1] - g)) / NeuQuant.alpharadbias;
                        this.network[j][2] -= (this.radpower[m] * (this.network[j][2] - r)) / NeuQuant.alpharadbias;

                        j++;
                    }
                    catch (Exception e)
                    {
                        // prevents 1.3 miscompilation
                    }
                }

                if (k > lo)
                {
                    try
                    {
                        this.network[k][0] -= (this.radpower[m] * (this.network[k][0] - b)) / NeuQuant.alpharadbias;
                        this.network[k][1] -= (this.radpower[m] * (this.network[k][1] - g)) / NeuQuant.alpharadbias;
                        this.network[k][2] -= (this.radpower[m] * (this.network[k][2] - r)) / NeuQuant.alpharadbias;

                        k--;
                    }
                    catch (Exception e)
                    {
                    }
                }

                m++;
            }
        }

        /// <summary>
        /// Move neuron i towards biased (b,g,r) by factor alpha
        /// </summary>
        void AlterSingle(int alpha, int i, int b, int g, int r)
        {
            this.network[i][0] -= (alpha * (this.network[i][0] - b)) / NeuQuant.initalpha;
            this.network[i][1] -= (alpha * (this.network[i][1] - g)) / NeuQuant.initalpha;
            this.network[i][2] -= (alpha * (this.network[i][2] - r)) / NeuQuant.initalpha;
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

            int bestd = ~(((int)1) << 31);
            int bestbiasd = bestd;
            int bestpos = -1;
            int bestbiaspos = bestpos;

            for (int i = 0; i < netsize; i++)
            {
                int dist = Math.Abs(this.network[i][0] - b);
                int a = Math.Abs(this.network[i][1] - g);

                dist += a;

                a = Math.Abs(this.network[i][2] - r);

                dist += a;

                if (dist < bestd)
                {
                    bestd = dist;
                    bestpos = i;
                }

                int biasdist = dist - ((this.bias[i]) >> (NeuQuant.intbiasshift - NeuQuant.netbiasshift));

                if (biasdist < bestbiasd)
                {
                    bestbiasd = biasdist;
                    bestbiaspos = i;
                }

                int betafreq = this.freq[i] >> NeuQuant.betashift;
                this.freq[i] -= betafreq;
                this.bias[i] += betafreq << NeuQuant.gammashift;
            }

            this.freq[bestpos] += NeuQuant.beta;
            this.bias[bestpos] -= NeuQuant.betagamma;

            return bestbiaspos;
        }
    }
}
