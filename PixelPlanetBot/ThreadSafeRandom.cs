using System;

namespace PixelPlanetBot
{
    class ThreadSafeRandom
    {
        [ThreadStatic]
        private static Random threadInstance;

        public static double NextDouble()
        {
            Random random = threadInstance;
            if (threadInstance == null)
            {
                int seed = Guid.NewGuid().GetHashCode();
                threadInstance = random = new Random(seed);
            }
            return random.NextDouble();
        }

        public static int Next(int minValue, int maxValue)
        {
            Random random = threadInstance;
            if (threadInstance == null)
            {
                int seed = Guid.NewGuid().GetHashCode();
                threadInstance = random = new Random(seed);
            }
            return random.Next(minValue, maxValue);
        }
    }
}
