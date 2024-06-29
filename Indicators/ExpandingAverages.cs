
namespace BinanceTestnet.Indicators
{
    public static class ExpandingAverages
    {
public static int CheckSMAExpansion(List<double> sma14, List<double> sma50, List<double> sma100, List<double> sma200, int index)
{
    if (index < 5) return 0;

    bool isUpwardExpansion = 
        sma50[index] > sma100[index] 
        && sma100[index] > sma200[index]
        && (sma50[index] - sma50[index - 3]) > (sma100[index] - sma100[index - 3])
        && (sma100[index] - sma100[index - 3]) > (sma200[index] - sma200[index - 3]);

    bool isDownwardExpansion = 
        sma50[index] < sma100[index] 
        && sma100[index] < sma200[index]
        && (sma50[index - 3] - sma50[index]) > (sma100[index - 3] - sma100[index])
        && (sma100[index - 3] - sma100[index]) > (sma200[index - 3] - sma200[index]);

    if (isUpwardExpansion) return 1;
    if (isDownwardExpansion) return -1;
    return 0;
}


        public static int CheckSMAExpansionEasy(List<double> sma14, List<double> sma50, List<double> sma100, List<double> sma200, int index)
        {

            if (index < 2) return 0;


            bool isUpwardExpansion = sma50[index] > sma100[index] && sma50[index - 2] < sma50[index] && sma100[index - 2] < sma100[index] &&
                                     (sma50[index] - sma50[index - 2]) > (sma100[index] - sma100[index - 2]);

            bool isDownwardExpansion = sma50[index] < sma100[index] && sma50[index - 2] > sma50[index] && sma100[index - 2] > sma100[index] &&
                                     (sma50[index] - sma50[index - 2]) < (sma100[index] - sma100[index - 2]);;

            if (isUpwardExpansion) return 1;
            if (isDownwardExpansion) return -1;
            return 0;
        }
    }
}
