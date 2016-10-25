using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SankakuChannelAPI;

namespace TestingConsole
{
    class Program
    {
        static void Main(string[] args)
        {                 
            var user = new SankakuChannelUser("CryADsisAM", "0606997500173");
            user.Authenticate();

            var list = user.Search("large_breasts", 1, 15);
            Console.WriteLine($"Found {list.Count} posts.");
            var content = list[1].DownloadFullImage(out bool wasRedirected);
            Console.ReadLine();
        }
    }
}
