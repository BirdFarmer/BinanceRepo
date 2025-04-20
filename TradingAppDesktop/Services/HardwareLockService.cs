using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TradingAppDesktop.Services
{
    public static class HardwareLockService
    {
        private const string GoogleSheetUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vQLUBoPccPsFNIdwIPFVBgN6nzDAa5O1GeTTF8sopH1E_6VS4032ge2MCYf3fUnS_h-Hn4_zl9L7Gh0/pub?output=csv";
        
        private static readonly string CacheFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TradingBot",
            "license.dat");

        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(2); 

        public static async Task<(bool isApproved, string deviceId)> CheckApprovalAsync()
        {
            var deviceId = GenerateStableDeviceId();
            Debug.WriteLine($"Device ID: {deviceId}");

            if (IsCachedLicenseValid(deviceId))
            {
                Debug.WriteLine("Valid cache found");
                return (true, deviceId);
            }

            Debug.WriteLine("Performing fresh sheet check...");
            bool isApproved = await CheckGoogleSheetApproval(deviceId);
            Debug.WriteLine($"Approval result: {isApproved}");

            if (isApproved)
            {
                Debug.WriteLine("Updating cache...");
                await WriteLicenseCache(deviceId);
            }
            
            return (isApproved, deviceId);
        }

        private static bool IsCachedLicenseValid(string deviceId)
        {
            try
            {
                if (!File.Exists(CacheFile)) 
                {
                    Debug.WriteLine("No cache file exists");
                    return false;
                }
                
                var content = File.ReadAllText(CacheFile);
                var parts = content.Split('|');
                
                if (parts.Length != 2)
                {
                    Debug.WriteLine("Invalid cache format");
                    return false;
                }

                var cacheDeviceId = parts[0];
                var cacheTime = DateTime.Parse(parts[1]);
                var age = DateTime.UtcNow - cacheTime;
                
                Debug.WriteLine($"Cache age: {age.TotalMinutes:0.0} minutes");
                
                return cacheDeviceId == deviceId && age < CacheDuration;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cache check failed: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> CheckGoogleSheetApproval(string deviceId)
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            
            try
            {
                // Bypass Google's cache
                string csv = await http.GetStringAsync($"{GoogleSheetUrl}&rand={Guid.NewGuid()}");
                var approvedIds = ParseApprovedIds(csv).ToList();
                return approvedIds.Contains(deviceId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Sheet check failed: {ex.Message}");
                return false;
            }
        }

        private static IEnumerable<string> ParseApprovedIds(string csv)
        {
            return csv.Split('\n')
                .Skip(1)
                .Select(line => line.Split(',')[0].Trim())
                .Where(id => !string.IsNullOrEmpty(id));
        }

        private static async Task WriteLicenseCache(string deviceId)
        {
            const int maxRetries = 3;
            int attempt = 0;
            
            while (attempt < maxRetries)
            {
                try
                {
                    var tempFile = CacheFile + ".tmp";
                    var cacheDir = Path.GetDirectoryName(CacheFile);
                    
                    Directory.CreateDirectory(cacheDir);
                    await File.WriteAllTextAsync(tempFile, $"{deviceId}|{DateTime.UtcNow}");
                    
                    File.Move(tempFile, CacheFile, overwrite: true);
                    Debug.WriteLine("Cache write successful");
                    return;
                }
                catch (Exception ex)
                {
                    attempt++;
                    Debug.WriteLine($"Cache write attempt {attempt} failed: {ex.Message}");
                    await Task.Delay(100);
                }
            }
        }

        public static string GenerateStableDeviceId()
        {
            // These 4 factors provide best balance
            string[] components = new string[]
            {
                Environment.MachineName,    // Identifies machine
                GetMacAddress(),           // Hardware-bound
                Environment.ProcessorCount.ToString(), // Very stable
                GetDriveType("C")          // Just drive type (NTFS/FAT/etc)
            };

            using var sha256 = SHA256.Create();
            var input = string.Join("|", components) + "|YOUR_SECRET_SALT";
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            
            return BitConverter.ToString(hash).Replace("-", "");
        }

        private static string GetDriveType(string drive)
        {
            try
            {
                return new DriveInfo(drive).DriveType.ToString();
            }
            catch
            {
                return "UNKNOWN_DRIVE";
            }
        }

        private static string GetMacAddress()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                    .Select(nic => nic.GetPhysicalAddress().ToString())
                    .FirstOrDefault() ?? "NO_MAC";
            }
            catch
            {
                return "MAC_FAILED";
            }
        }

        public static string GetActivationMessage(string deviceId) => 
            $"Contact support with this ID:\n\n{deviceId}\n\n(Press Ctrl+C to copy)";
    }
}