// Quest (IL2CPP) only. The shared source files say "using Alta.X;" and name the game's global-namespace types (BowString, Player...)
// bare. On Quest the same types live in Il2CppAlta.X and in the namespace Il2Cpp. This file makes the shared files' own lines
// compile unchanged: the PC namespaces exist here as empty ones, and the Quest ones are imported everywhere.
global using Il2Cpp;
global using Il2CppAlta.Networking;
global using Il2CppAlta.Impact;
global using Il2CppAlta.Networking.Scripts.Player;
global using Il2CppAlta.Progression.Skills;
global using Il2CppAlta.Inventory;
global using Il2CppAlta.Global;
global using Il2CppAlta.StatSystem;
global using Il2CppAlta.Serialization;
global using Il2CppAlta.Networking.Servers;
global using Il2CppAlta.Utilities;
global using Il2CppAlta.Environmental.Atmosphere;
global using Il2CppAlta.Console;
global using Il2CppAlta.Chunks;
global using Il2CppATT.Saving;

namespace Alta.Networking { }
namespace Alta.Impact { }
namespace Alta.Networking.Scripts.Player { }
namespace Alta.Progression.Skills { }
namespace Alta.Inventory { }
namespace Alta.Global { }
namespace Alta.StatSystem { }
namespace Alta.Serialization { }
namespace Alta.Networking.Servers { }
namespace Alta.Utilities { }
namespace Alta.Environmental.Atmosphere { }
namespace Alta.Console { }
namespace Alta.Chunks { }
namespace ATT.Saving { }
