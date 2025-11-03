using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SkiaSharp;

public class TimeEntry
{
    public string? Id { get; set; }
    public string? EmployeeName { get; set; }
    public string? StarTimeUtc { get; set; }
    public string? EndTimeUtc { get; set; }
    public string? EntryNotes { get; set; }
}

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("Starting RAPIDD assignment program...");

        string apiUrl = "https://rc-vault-fap-live-1.azurewebsites.net/api/gettimeentries?code=vO17RnE8vuzXzPJo5eaLLjXjmRW07";
        if (args.Length > 0)
            apiUrl = args[0];

        try
        {
            var entries = await FetchEntries(apiUrl);
            if (entries == null || entries.Count == 0)
            {
                Console.WriteLine("No entries received from API.");
                return 1;
            }

            var agg = AggregateHours(entries);

            string htmlPath = Path.Combine(Directory.GetCurrentDirectory(), "output.html");
            File.WriteAllText(htmlPath, GenerateHtmlTable(agg));
            Console.WriteLine($"Wrote HTML output to {htmlPath}");

            string chartPath = Path.Combine(Directory.GetCurrentDirectory(), "chart.png");
            GeneratePieChart(agg, chartPath);
            Console.WriteLine($"Wrote pie chart to {chartPath}");

            Console.WriteLine("Done.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            return 2;
        }
    }

    static async Task<List<TimeEntry>?> FetchEntries(string url)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        Console.WriteLine("Fetching JSON from API...");
        var resp = await client.GetStringAsync(url);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<TimeEntry>>(resp, opts);
    }

    static Dictionary<string, double> AggregateHours(List<TimeEntry> entries)
    {
        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.EmployeeName))
                continue;

            if (!TryParseDate(e.StarTimeUtc, out var start) || !TryParseDate(e.EndTimeUtc, out var end))
                continue;

            var duration = end - start;
            if (duration.TotalSeconds < 0)
                duration = duration.Negate();

            double hours = duration.TotalHours;
            if (hours < 0) hours = 0;

            if (!dict.ContainsKey(e.EmployeeName)) dict[e.EmployeeName] = 0;
            dict[e.EmployeeName] += hours;
        }

        return dict.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2));
    }

    static bool TryParseDate(string? s, out DateTime dt)
    {
        dt = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(s)) return false;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt);
    }

    static string GenerateHtmlTable(Dictionary<string, double> agg)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">\n<title>Time Worked - RAPIDD Assignment</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: Arial, Helvetica, sans-serif; padding: 20px; }\n.table { border-collapse: collapse; width: 600px; }\n.table th, .table td { border: 1px solid #ddd; padding: 8px; text-align: left; }\n.table th { background-color: #f2f2f2; }\n.low { background-color: #ffe6e6; }\n</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<h2>Employees ordered by total time worked (hours)</h2>");
        sb.AppendLine("<table class=\"table\">\n<thead><tr><th>Name</th><th>Total Hours</th></tr></thead><tbody>");

        foreach (var kv in agg)
        {
            string cls = kv.Value < 100.0 ? " class=\"low\"" : "";
            sb.AppendLine($"<tr{cls}><td>{System.Net.WebUtility.HtmlEncode(kv.Key)}</td><td>{kv.Value:0.##}</td></tr>");
        }

        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    static void GeneratePieChart(Dictionary<string, double> agg, string outPath)
    {
        if (agg == null || agg.Count == 0)
            throw new ArgumentException("No data to draw chart.");

        int width = 900;
        int height = 600;
        
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        
        // Clear with white background
        canvas.Clear(SKColors.White);

        double total = agg.Values.Sum();
        if (total <= 0) total = 1; // avoid divide by zero

        // Colors (SkiaSharp colors)
        SKColor[] palette = new SKColor[] {
            new SKColor(0x4E, 0x79, 0xA7), new SKColor(0xC8, 0x5A, 0x5A), new SKColor(0x7F, 0xB5, 0x8C),
            new SKColor(0xF2, 0xC2, 0x4B), new SKColor(0xA4, 0x90, 0xCA), new SKColor(0xE7, 0x87, 0xB1),
            new SKColor(0x6E, 0xCE, 0xE0), new SKColor(0xD0, 0xD0, 0xD0)
        };

        // Pie chart area
        SKRect pieRect = new SKRect(30, 30, 450, 450);
        float startAngle = 0f;
        int i = 0;

        // Draw slices
        foreach (var kv in agg)
        {
            double percent = kv.Value / total;
            float sweep = (float)(percent * 360.0);
            
            using var paint = new SKPaint 
            { 
                Color = palette[i % palette.Length],
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            
            using var path = new SKPath();
            path.AddOval(pieRect);
            path.MoveTo(pieRect.MidX, pieRect.MidY);
            path.ArcTo(pieRect, startAngle, sweep, false);
            path.Close();
            
            canvas.DrawPath(path, paint);
            
            startAngle += sweep;
            i++;
        }

        // Draw legend
        int legendX = 480;
        int legendY = 40;
        int legendBox = 24;
        i = 0;
        
        using var textFont = new SKFont(SKTypeface.FromFamilyName("Arial"), 14);
        using var boldFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), 16);

        foreach (var kv in agg)
        {
            using var paint = new SKPaint 
            { 
                Color = palette[i % palette.Length],
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            
            // Legend color box
            canvas.DrawRect(legendX, legendY + i * 30, legendBox, legendBox, paint);
            
            // Legend text
            string label = $"{kv.Key} — {kv.Value:0.##}h ({(kv.Value / total * 100.0):0.##}%)";
            canvas.DrawText(label, legendX + legendBox + 8, legendY + i * 30 + 18, SKTextAlign.Left, textFont, new SKPaint { Color = SKColors.Black, IsAntialias = true });
            i++;
        }

        // Title
        canvas.DrawText("Time Worked Distribution", 30, 500, SKTextAlign.Left, boldFont, new SKPaint { Color = SKColors.Black, IsAntialias = true });

        // Save as PNG
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outPath);
        data.SaveTo(stream);
    }
}
