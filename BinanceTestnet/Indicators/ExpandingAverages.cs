
namespace BinanceTestnet.Indicators
{
    public static class ExpandingAverages
    {
        public static int CheckSMAExpansion(List<double> sma25, List<double> sma50, List<double> sma100, List<double> sma200, int index)
        {
            if (index < 5) return 0;

            bool isUpwardExpansion = 
                sma50[index] > sma100[index] 
                && sma100[index] > sma200[index]
                && (sma50[index] - sma50[index - 1]) > 0
                && (sma100[index] - sma100[index - 1]) > 0
                && (sma200[index] - sma200[index -1]) >= 0
                //&& (sma50[index] - sma50[index - 1]) > (sma100[index] - sma100[index - 1]);
                //&& (sma100[index] - sma100[index - 1]) >= (sma200[index] - sma200[index -1]
                ;

            bool isDownwardExpansion = 
                sma50[index] < sma100[index]
                && sma100[index] < sma200[index]
                && (sma50[index] - sma50[index - 1]) < 0
                && (sma100[index] - sma100[index - 1]) < 0
                && (sma200[index] - sma200[index -1]) <= 0    
                //&& (sma50[index] - sma50[index - 1]) < (sma100[index] - sma100[index - 1]);
                //&& (sma100[index] - sma100[index - 1]) <= (sma200[index] - sma200[index - 1]
                ;


            if (isUpwardExpansion 
                && (sma25[index] <= sma25[index -1])) 
            {                    
                return 1;
                Console.WriteLine($"Fast SMAs are expanding UP faster than the slower ones, trying to go LONG");
            }
                     
            if (isDownwardExpansion 
                && (sma25[index] >= sma25[index -1])) 
            {
                return -1;
                Console.WriteLine($"Fast SMAs are expanding DOWN faster than the slower ones, trying to go SHORT");
            }
                
            return 0;
        }

        public static int CheckSMAExpansionEasy(List<double> sma14, List<double> sma50, List<double> sma100, List<double> sma200, int index)
        {
            if (index < 200) return 0;

            bool isUpwardExpansion = sma50[index] > sma100[index] 
                                        && sma100[index] > sma100[index - 2] 
                                        && sma50[index] > sma50[index - 2] 
                                        && (sma50[index] - sma50[index - 2]) > (sma100[index] - sma100[index - 2]);

            bool isDownwardExpansion = sma50[index] < sma100[index] 
                                        && sma50[index] < sma50[index - 2] 
                                        && sma100[index] < sma100[index - 2] 
                                        && (sma50[index] - sma50[index - 2]) < (sma100[index] - sma100[index - 2]);

            if (isUpwardExpansion) 
            {                    
                return 1;
                Console.WriteLine($"SMA50 is expanding UP faster than SMA100, trying to go LONG");
            }
                     
            if (isDownwardExpansion) 
            {
                return -1;
                Console.WriteLine($"SMA50 is expanding DOWN faster than SMA100, trying to go SHORT");
            }
            return 0;
        }

        public static int ConfirmThe200Turn(List<double> sma14, List<double> sma50, List<double> sma100, List<double> sma200, int index)
        {
            if (index < 200) return 0;

            bool isTurningUp = sma200[index - 8] > sma200[index - 7]
                                && sma200[index - 7] <= sma200[index - 6]
                                && sma200[index - 6] <= sma200[index - 5]
                                && sma200[index - 5] <= sma200[index - 4]
                                && sma200[index - 4] <= sma200[index - 3]
                                && sma200[index - 3] <= sma200[index - 2]
                                && sma200[index - 2] <= sma200[index - 1]
                                && sma200[index - 1] < sma200[index];

            bool isTurningDown = sma200[index - 8] < sma200[index - 7]
                                && sma200[index - 7] >= sma200[index - 6]
                                && sma200[index - 6] >= sma200[index - 5]
                                && sma200[index - 5] >= sma200[index - 4]
                                && sma200[index - 4] >= sma200[index - 3]
                                && sma200[index - 3] >= sma200[index - 2]
                                && sma200[index - 2] >= sma200[index - 1]
                                && sma200[index - 1] > sma200[index];


            if (isTurningUp) 
            {                    
                return 1;
                Console.WriteLine($"SMA200 it turning upwards, trying to go LONG");
            }
                     
            if (isTurningDown) 
            {
                return -1;
                Console.WriteLine($"SMA200 is turning downwards, trying to go SHORT");
            }
            return 0;
        }

    }
}
