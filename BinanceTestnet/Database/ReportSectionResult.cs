
namespace BinanceTestnet.Database
{
    public class ReportSectionResult
    {
        public string Html { get; set; } = string.Empty;
        public string SectionName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        
        public static ReportSectionResult CreateSuccess(string sectionName, string html)
        {
            return new ReportSectionResult 
            { 
                SectionName = sectionName, 
                Html = html, 
                Success = true 
            };
        }
        
        public static ReportSectionResult CreateError(string sectionName, string error)
        {
            return new ReportSectionResult 
            { 
                SectionName = sectionName,
                ErrorMessage = error, 
                Success = false,
                Html = $$"""
                    <div class="section">
                        <h2>⚠️ {{sectionName}}</h2>
                        <div class="warning">
                            <strong>Section temporarily unavailable:</strong> {{error}}
                            <br><em>This section will be restored when the data source is available.</em>
                        </div>
                    </div>
                """
            };
        }
    }
}