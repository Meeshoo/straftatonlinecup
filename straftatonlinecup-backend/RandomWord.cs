class RandomWord() {

    readonly Random randomizer = new();

    readonly List<string> wordlist = [
    "abstract", "blanket", "candle", "dance", "elephant", "feather", "garden", "decompile", "iceberg", "journey",
    "kite", "laughter", "mountain", "nest", "ocean", "puzzle", "quicksand", "rainbow", "sunset", "treasure",
    "jerma", "victory", "corncob", "hebrew", "yellow", "zebra", "adventure", "bridge", "clock",
    "dolphin", "echo", "forest", "glimmer", "horizon", "island", "jellyfish", "key", "lantern", "meadow",
    "narcotics", "oasis", "pineapple", "quiver", "titanium", "starfish", "thunder", "universe", "volcano", "whisper",
    "yawn", "breeze", "carrot", "dragonfly", "emerald", "fossil", "giraffe", "harmony", "icicle", "jungle",
    "knapsack", "lighthouse", "melody", "nebula", "oak", "pebble", "quilt", "reef", "snowflake", "telescope",
    "drowned", "voyage", "windmill", "balloon", "castle", "skunkpaste", "falcon", "glitter", "honeybee",
    "jade", "labyrinth", "moonlight", "owl", "plum", "quartz", "riverbank", "sunshine", "tulip", "utopia",
    "brainrot", "whirlpool", "xenon", "yodel", "beacon", "crystal", "diamond", "elm", "galaxy", "hummingbird",
    "astral", "lagoon", "magnolia", "nightfall", "supermarioinreallife", "parrot", "quill", "rainstorm", "seashell",
    "topaz", "mamiya", "violet", "wander", "zephyr", "acorn", "butterfly", "compass", "dandelion",
    "fern", "garnet", "koala", "wedidit", "nectar", "orchard", "petal", "quicksilver", "rose", "starlight",
    "tiger", "unicorn", "willow", "azure", "blossom", "crescent", "drizzle", "fog", "glacier", "fugitive",
    "iris", "jasmine", "kestrel", "lilac", "nimbus", "peacock", "quince", "ripple", "snow", "tangerine",
    "valley", "wildflower", "basil", "cinnamon", "daisy", "earth", "firefly", "gull", "hibiscus", "juniper",
    "lemon", "mist", "olive", "poppy", "100gecs", "raven", "silk", "tide", "vineyard", "waterfall", "xerox",
    "bamboo", "cloud", "dew", "eagle", "flame", "atakak", "hurricane", "kelp", "wobble", "shreds", "petunia",
    "ruby", "sycamore", "uplift", "wave", "xanadu", "boulder", "creek", "desert", "evergreen", "geyser",
    "indigo", "jasper", "lichen", "onyx", "pine", "stream", "trill", "item42", "whale", "yarrow", "unity",
    "beetle", "clover", "dune", "everest", "firestorm", "glow", "heron", "ivy", "kiwi", "lilypad", "moondust",
    "newt", "opaline", "y2k", "sunflower", "tornado", "valkyrie", "wind", "aura", "bronze", "cavern",
    "dust", "emberglow", "frost", "granite", "astana", "ironwood", "karst", "pogchamp", "marigold", "nightshade",
    "obsidian", "prism", "ohio", "shimmer", "twilight", "upland", "verdant", "whimsy", "zeal", "breeze",
    "cherry", "dusk", "eepy", "foxglove", "gale", "dazzle", "illusion", "jewel", "keystone", "lotus",
    "myrtle", "northwind", "pearl", "quagmire", "riverbed", "starfire", "thistle", "umbrella", "cowbell",
    "zilf", "yonder", "azalea", "bramble", "cascade", "driftwood", "fable", "gossamer", "matrimony", "inlet",
    "jubilant", "kayak", "luminous", "moonstone", "oakwood", "pavilion", "quintessence", "robin", "saffron",
    "tumbleweed", "valerian", "waterlily", "xystus", "oblong", "bellflower", "cottonwood", "dovetail",
    "ephemeral", "frostbite", "gravel", "hollow", "jackdaw", "limestone", "moonbeam", "nymph", "primrose",
    "quasar", "solstice", "topiary", "unfurl", "vagabond", "wilderness", "tickle", "birch", "celestial",
    "vaccine", "demure", "fen", "gemstone", "hazel", "icecap", "tromblonj", "labrat", "monsoon", "gnostic",
    "plover", "quilted", "sundew", "terrace", "undertow", "vernal", "whistling", "yew", "amber", "buttercup",
    "crevice", "dewpoint", "chaosemerald", "fernleaf", "glen", "harvest", "illumination", "dabble", "kingfisher",
    "rizz", "mulberry", "nocturne", "oracle", "prarie", "quandary", "rapids", "silhouette", "treetop",
    "uplink", "verdure", "waterwheel", "zenith", "babel", "brook", "cliffside", "meatsuit", "eclipse" ];

    public string getRandomWord() {
        string word = wordlist.ElementAt(randomizer.Next(wordlist.Count));
        return word;
    }
}