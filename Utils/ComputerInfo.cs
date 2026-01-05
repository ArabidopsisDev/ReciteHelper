using System.Management;

namespace ReciteHelper.Utils;


public class ComputerInfo
{
    public static string GetComputerBrand()
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    return obj["Manufacturer"]?.ToString() ?? "Unknown";
                }
            }
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
        return "Unknown";
    }
}
