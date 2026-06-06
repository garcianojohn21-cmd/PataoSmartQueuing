using WebPush;
using System;

namespace PataoSmartQueuing.Tools
{
    public class VapidKeyGenerator
    {
        public static void GenerateKeys()
        {
            Console.WriteLine("==============================================");
            Console.WriteLine("   VAPID Key Generator");
            Console.WriteLine("==============================================\n");

            var keys = VapidHelper.GenerateVapidKeys();

            Console.WriteLine("✅ Keys Generated Successfully!\n");
            Console.WriteLine("📋 Copy and paste this into your appsettings.json:\n");
            Console.WriteLine("\"WebPush\": {");
            Console.WriteLine($"  \"PublicKey\": \"{keys.PublicKey}\",");
            Console.WriteLine($"  \"PrivateKey\": \"{keys.PrivateKey}\",");
            Console.WriteLine("  \"Subject\": \"mailto:admin@pataonhs.edu\"");
            Console.WriteLine("},\n");
            Console.WriteLine("==============================================");
            Console.WriteLine("⚠️  SECURITY WARNING:");
            Console.WriteLine("==============================================");
            Console.WriteLine("- Keep your PRIVATE KEY secret!");
            Console.WriteLine("- Never commit it to source control");
            Console.WriteLine("- The PUBLIC KEY is safe to expose\n");
        }
    }
}