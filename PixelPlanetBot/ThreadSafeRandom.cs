using System;

namespace PixelPlanetBot
{
    class ThreadSafeRandom
    {
        [ThreadStatic]
        private static Random threadInstance;

        public static int Next()
        {
            Random random = threadInstance;
            if (threadInstance == null)
            {
                int seed = Guid.NewGuid().GetHashCode();
                threadInstance = random = new Random(seed);
            }
            return random.Next();
        }
    }
}
