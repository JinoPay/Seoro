namespace Seoro.Shared.Models;

public static class CityNames
{
    private static int _counter;

    private static readonly string[] Names =
    [
        // Asia
        "tokyo", "delhi", "seoul", "dhaka", "kobe",
        "nara", "pune", "agra", "kyoto", "osaka",
        "busan", "daegu", "suwon", "jeju", "ulsan",
        "davao", "cebu", "hanoi", "phuket", "bali",
        "hue", "goa", "taipei", "macau", "manila",
        "yangon", "phnom", "vigan", "dalat", "melaka",
        "jogja", "surat", "kochi", "jaipur", "varanasi",
        "lhasa", "guilin", "suzhou", "xiamen", "chengdu",
        "nanjing", "hefei", "wuhan", "fuzhou", "ningbo",
        "sendai", "nagoya", "fukuoka", "sapporo", "okinawa",
        "incheon", "gwangju", "daejeon", "pohang", "chuncheon",
        "baguio", "iloilo", "krabi", "chiang", "penang",
        "ipoh", "bandar", "batam", "lombok", "semarang",
        "lucknow", "indore", "nagpur", "patna", "madurai",

        // Middle East
        "dubai", "jeddah", "muscat", "amman", "doha",
        "baku", "sana", "aden", "beirut", "riyadh",
        "tabriz", "shiraz", "isfahan", "erbil", "basra",
        "aqaba", "haifa", "salalah", "nizwa", "yanbu",
        "abha", "taif", "hofuf", "dammam", "fujairah",

        // Europe
        "oslo", "rome", "lyon", "nice", "bern",
        "cork", "graz", "oulu", "perm", "omsk",
        "brno", "gent", "linz", "split", "porto",
        "siena", "lucca", "basel", "ghent", "bruges",
        "dijon", "reims", "tours", "lille", "mainz",
        "essen", "trier", "bilbao", "malaga", "cadiz",
        "vigo", "murcia", "genoa", "parma", "padua",
        "pisa", "bari", "turku", "tartu", "kiel",
        "york", "bath", "aarhus", "bergen", "gothenburg",
        "zurich", "lucerne", "innsbruck", "salzburg", "prague",
        "vienna", "budapest", "warsaw", "krakow", "gdansk",
        "riga", "vilnius", "tallinn", "dubrovnik", "kotor",
        "mostar", "plovdiv", "sintra", "aveiro", "granada",
        "seville", "toledo", "pamplona", "san sebastian", "florence",
        "naples", "verona", "bologna", "turin", "catania",
        "valletta", "nicosia", "rhodes", "corfu", "crete",
        "delft", "leiden", "utrecht", "antwerp", "leuven",
        "colmar", "annecy", "avignon", "nantes", "bordeaux",
        "dresden", "leipzig", "heidelberg", "freiburg", "bamberg",
        "stavanger", "tromso", "rovaniemi", "tampere", "copenhagen",

        // Africa
        "cairo", "tunis", "rabat", "accra", "dakar",
        "nairobi", "kampala", "lusaka", "maputo", "harare",
        "lome", "juba", "fez", "marrakech", "luxor",
        "aswan", "mombasa", "zanzibar", "arusha", "kigali",
        "windhoek", "gaborone", "durban", "cape town", "stellenbosch",
        "essaouira", "chefchaouen", "djerba", "oran", "algiers",
        "addis", "lalibela", "lamu", "malindi", "toliara",

        // Americas
        "lima", "cusco", "quito", "sucre", "denver",
        "austin", "miami", "tampa", "tulsa", "boise",
        "provo", "ogden", "fargo", "akron", "flint",
        "plano", "irvine", "fresno", "tacoma", "reno",
        "mesa", "waco", "erie", "nome", "vail",
        "elko", "ames", "bend", "cody", "aruba",
        "bondi", "savannah", "charleston", "portland", "seattle",
        "boulder", "sedona", "aspen", "telluride", "carmel",
        "monterey", "quebec", "halifax", "banff", "jasper",
        "cusco", "medellin", "cartagena", "bogota", "santiago",
        "valparaiso", "mendoza", "bariloche", "montevideo", "recife",
        "floripa", "oaxaca", "merida", "tulum", "antigua",
        "havana", "nassau", "kingston", "curacao", "tobago",

        // Oceania
        "suva", "apia", "perth", "darwin", "cairns",
        "hobart", "adelaide", "queenstown", "rotorua", "napier",
        "nelson", "dunedin", "wollongong", "noumea", "nadi",
        "tonga", "samoa", "palau", "guam", "rarotonga"
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