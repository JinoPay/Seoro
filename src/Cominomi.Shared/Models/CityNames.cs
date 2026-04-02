namespace Cominomi.Shared.Models;

public static class CityNames
{
    private static int _counter;

    private static readonly string[] Names =
    [
        "tokyo", "delhi", "seoul", "cairo", "lima",
        "dhaka", "oslo", "doha", "baku", "rome",
        "lyon", "nice", "bern", "cork", "graz",
        "kobe", "nara", "pune", "agra", "sana",
        "aden", "suva", "lome", "goa", "hue",
        "fez", "apia", "mali", "juba", "kiel",
        "troy", "york", "bath", "reno", "mesa",
        "waco", "gary", "erie", "troy", "nome",
        "vail", "elko", "ames", "bend", "cody",
        "oulu", "perm", "omsk", "brno", "gent",
        "linz", "malm", "turku", "tartu", "split",
        "porto", "siena", "lucca", "Basel", "ghent",
        "bruges", "dijon", "reims", "tours", "lille",
        "mainz", "essen", "trier", "kyoto", "osaka",
        "busan", "daegu", "suwon", "jeju", "ulsan",
        "cusco", "quito", "sucre", "aruba", "bondi",
        "davao", "cebu", "hanoi", "hochi", "phuket",
        "bali", "dubai", "jeddah", "muscat", "amman",
        "beirut", "tunis", "rabat", "accra", "dakar",
        "nairobi", "kampala", "lusaka", "maputo", "harare",
        "denver", "austin", "miami", "tampa", "tulsa",
        "boise", "provo", "ogden", "fargo", "akron",
        "flint", "plano", "irvine", "fresno", "tacoma",
        "bilbao", "malaga", "cadiz", "vigo", "murcia",
        "genoa", "parma", "padua", "pisa", "bari"
    ];

    public static string GetNext()
    {
        var idx = Interlocked.Increment(ref _counter) - 1;
        return Names[idx % Names.Length];
    }

    public static string GetRandom()
    {
        return Names[Random.Shared.Next(Names.Length)];
    }
}