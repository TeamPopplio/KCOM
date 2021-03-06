/*
    Kiwi's Co-Op Mod for Half-Life: Alyx
    Copyright (c) 2022 KiwifruitDev
    All rights reserved.
    This software is licensed under the MIT License.
    -----------------------------------------------------------------------------
    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
    -----------------------------------------------------------------------------
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using KiwisCoOpModCore;
using Fleck;
using System.Reflection;

namespace KiwisCoOpMod
{
    public class ServerProgram
    {
        public static readonly ServerProgram instance = new();
        public string map = "";
        public WebSocketServer? wss;
        public List<IndexedClient> connections = new() { };
        public Type? gamemodeType;
        public List<Type> plugins = new();
        public List<ICustomizationOption> customizationOptions = new();
        public void Start(Type type, List<Type> plugins)
        {
            if (wss == null)
            {
                PluginHandler.Handle(plugins, PluginHandleType.Server_PreGamemode_PreStart);
                if (GamemodeHandler.Handle(type, GamemodeHandleType.PreStart) == HandleState.Continue)
                {
                    map = Settings.Default.ServerMap;
                    this.plugins = plugins;
                    PluginHandler.Handle(plugins, PluginHandleType.Server_PostGamemode_PreStart, type, plugins);
                    gamemodeType = type;
                    wss = new WebSocketServer("ws://" + Settings.Default.ServerIpAddress + ":" + Settings.Default.ServerPort);
                    wss.Start(socket =>
                    {
                        socket.OnOpen = () => OnClose(socket);
                        socket.OnClose = () => OnClose(socket);
                        socket.OnMessage = message => OnMessage(message, socket);
                    });
                    if (gamemodeType != null)
                    {
                        PluginHandler.Handle(plugins, PluginHandleType.Server_PreGamemode_PostStart, gamemodeType, plugins);
                        GamemodeHandler.Handle(gamemodeType, GamemodeHandleType.PostStart, gamemodeType, plugins);
                        PluginHandler.Handle(plugins, PluginHandleType.Server_PostGamemode_PostStart, gamemodeType, plugins);
                    }
                    string? appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (appDir != null)
                    {
                        // Load character customization options in gamemodes
                        string gamemodesFolderPath = Path.Combine(appDir, "gamemodes");
                        Directory.CreateDirectory(gamemodesFolderPath);
                        string[] files = Directory.GetFiles(gamemodesFolderPath, "*.dll", SearchOption.TopDirectoryOnly);
                        foreach (string dll in files)
                        {
                            LoadFromDllFile(appDir, dll);
                        }
                        // Load character customization options in plugins
                        string pluginsFolderPath = Path.Combine(appDir, "plugins");
                        Directory.CreateDirectory(pluginsFolderPath);
                        string[] pluginFiles = Directory.GetFiles(pluginsFolderPath, "*.dll", SearchOption.TopDirectoryOnly);
                        foreach (string dll in pluginFiles)
                        {
                            LoadFromDllFile(appDir, dll);
                        }
                    }
                }
            }
        }
        private void LoadFromDllFile(string appDir, string dll)
        {
            Assembly gamemodeAssembly = Assembly.LoadFrom(Path.Combine(appDir, dll));
            foreach (Type assemblyType in gamemodeAssembly.GetTypes())
            {
                if (assemblyType.GetInterface("ICustomizationOption") != null)
                {
                    ICustomizationOption? customizationOption = (ICustomizationOption?)Activator.CreateInstance(assemblyType);
                    if (customizationOption != null)
                    {
                        customizationOptions.Add(customizationOption);
                    }
                }
            }
        }
        public void Close()
        {
            if (wss != null && gamemodeType != null)
            {
                PluginHandler.Handle(plugins, PluginHandleType.Server_PreGamemode_PreClose);
                if (GamemodeHandler.Handle(gamemodeType, GamemodeHandleType.PreClose, wss) == HandleState.Continue)
                {
                    wss.Dispose();
                    wss = null;
                    PluginHandler.Handle(plugins, PluginHandleType.Server_PreGamemode_PostClose, gamemodeType, plugins);
                    GamemodeHandler.Handle(gamemodeType, GamemodeHandleType.PostClose, gamemodeType, plugins);
                    PluginHandler.Handle(plugins, PluginHandleType.Server_PostGamemode_PostClose, gamemodeType, plugins);
                }
            }
        }
        public void ChangeMap(string map)
        {
            this.map = map;
            Response output2 = new("command", "addon_play "+map+"; addon_tools_map "+map);
            foreach (IndexedClient con in connections)
            {
                con.Session.Send(JsonConvert.SerializeObject(output2));
            }
        }
        public void OnOpen(IWebSocketConnection socket)
        {
            if (gamemodeType != null)
            {
                PluginHandler.Handle(plugins, PluginHandleType.Server_PreGamemode_ClientOpen, connections, socket);
                GamemodeHandler.Handle(gamemodeType, GamemodeHandleType.ClientOpen, connections, socket);
                PluginHandler.Handle(plugins, PluginHandleType.Server_PostGamemode_ClientOpen, connections, socket);
            }
        }
        public void OnClose(IWebSocketConnection socket)
        {
            if (gamemodeType != null)
            {
                PluginHandler.Handle(plugins, PluginHandleType.Server_PreGamemode_ClientClose, connections, socket);
                GamemodeHandler.Handle(gamemodeType, GamemodeHandleType.ClientClose, connections, socket);
                PluginHandler.Handle(plugins, PluginHandleType.Server_PostGamemode_ClientClose, connections, socket);
            }
        }
        public void OnMessage(string message, IWebSocketConnection socket)
        {
            Response? response = JsonConvert.DeserializeObject<Response>(message);
            if (gamemodeType != null && response != null && response.type != null)
            {
                PluginHandler.Handle(plugins, PluginHandleType.Server_PreGamemode_PreResponse, response, connections, socket, map, customizationOptions);
                if (GamemodeHandler.Handle(gamemodeType, GamemodeHandleType.PreResponse, response, connections, socket, map, customizationOptions) == HandleState.Continue)
                {
                    PluginHandler.Handle(plugins, PluginHandleType.Server_PostGamemode_PreResponse, response, connections, socket, map, customizationOptions);
                    switch (response.type.ToLower())
                    {
                        case "client":
                            if (response.clientUsername != null && response.clientAuthId != null && response.password != null)
                            {
                                if (Settings.Default.ServerPassword != "" && response.password != Settings.Default.ServerPassword)
                                {
                                    Response output2 = new("status", "Invalid server password! Closing connection...");
                                    socket.Send(JsonConvert.SerializeObject(output2));
                                }
                                else
                                {
                                    foreach (IndexedClient indexed1 in connections)
                                    {
                                        if (indexed1.Username == response.clientUsername && indexed1.AuthId != response.clientAuthId)
                                        {
                                            Response output2 = new("status", "A client is already connected with this username: " + response.clientUsername);
                                            socket.Send(JsonConvert.SerializeObject(output2));
                                            socket.Close();
                                            break;
                                        }
                                        else if (indexed1.Username == response.clientUsername)
                                        {
                                            Response output2 = new("status", "Client reconnected elsewhere, closing connection...");
                                            socket.Send(JsonConvert.SerializeObject(output2));
                                            socket.Close();
                                            connections.Remove(indexed1);
                                            break;
                                        }
                                    }
                                    if(response.clientUsername != null && response.clientAuthId != null)
                                    {
                                        Response output2 = new("authenticated")
                                        {
                                            clientUsername = response.clientUsername,
                                            clientAuthId = response.clientAuthId,
                                            map = map,
                                            customizationOptionsName = new string[customizationOptions.Count],
                                            customizationOptionsDescription = new string[customizationOptions.Count],
                                            customizationOptionsAuthor = new string[customizationOptions.Count],
                                            customizationOptionsImage = new string[customizationOptions.Count],
                                            customizationOptionsDefault = new bool[customizationOptions.Count],
                                            customizationOptionsType = new string[customizationOptions.Count],
                                            customizationOptionsModelName = new string[customizationOptions.Count],
                                        };
                                        for(int i = 0; i < customizationOptions.Count; i++)
                                        {
                                            output2.customizationOptionsName[i] = customizationOptions[i].Name;
                                            output2.customizationOptionsDescription[i] = customizationOptions[i].Description;
                                            output2.customizationOptionsAuthor[i] = customizationOptions[i].Author;
                                            output2.customizationOptionsImage[i] = customizationOptions[i].DisplayImageBase64;
                                            output2.customizationOptionsDefault[i] = customizationOptions[i].Default;
                                            output2.customizationOptionsModelName[i] = customizationOptions[i].ModelName;
                                            output2.customizationOptionsType[i] = customizationOptions[i].Type switch
                                            {
                                                CustomizationOptionType.Head => "head",
                                                CustomizationOptionType.LeftHand => "lefthand",
                                                CustomizationOptionType.RightHand => "righthand",
                                                CustomizationOptionType.Hat => "hat",
                                                CustomizationOptionType.Collider => "collider",
                                                _ => "none",
                                            };
                                        }
                                        connections.Add(new IndexedClient(socket, response.clientUsername, response.clientAuthId, map));
                                        socket.Send(JsonConvert.SerializeObject(output2));
                                    }
                                }
                            }
                            else
                            {
                                Response output2 = new("status", "Invalid username and/or AuthID! Closing connection...");
                                socket.Send(JsonConvert.SerializeObject(output2));
                                socket.Close();
                            }
                            break;
                        case "chat":
                            IndexedClient? indexedClient = connections.Find(c => c.Session.ConnectionInfo.Id == socket.ConnectionInfo.Id);
                            if (indexedClient != null)
                            {
                                if (response.data != null)
                                {
                                    if (response.data.StartsWith("/"))
                                    {
                                        string command = response.data.Split(" ").ToArray().First().Replace("/", "").ToLower();
                                        string[] args = response.data.Split(" ").Skip(1).ToArray();
                                        Response output3 = new("status", response.data)
                                        {
                                            clientUsername = indexedClient.Username,
                                            clientAuthId = indexedClient.AuthId,
                                            data = "Unknown command '" + command + "'"
                                        };
                                        switch (command)
                                        {
                                            case "vc":
                                                if (!Settings.Default.ServerDisableUserVconsoleInput)
                                                {
                                                    output3 = new Response("command", response.data)
                                                    {
                                                        data = string.Join(" ", args),
                                                        urgent = false
                                                    };
                                                }
                                                else
                                                {
                                                    output3 = new Response("status", response.data)
                                                    {
                                                        data = "VConsole input is disabled on this server."
                                                    };
                                                }
                                                break;
                                        }
                                        socket.Send(JsonConvert.SerializeObject(output3));
                                    }
                                    else
                                    {
                                        Response output4 = new("chat", response.data)
                                        {
                                            remoteClientUsername = indexedClient.Username
                                        };
                                        connections.ForEach(c => c.Session.Send(JsonConvert.SerializeObject(output4)));
                                    }
                                }
                            }
                            break;
                        case "print":
                            IndexedClient? indexed2 = connections.Find(c => c.Session.ConnectionInfo.Id == socket.ConnectionInfo.Id);
                            if (indexed2 != null)
                            {
                                if (response.data != null)
                                {
                                    if (indexed2 != null && indexed2 != null)
                                    {
                                        Response output5 = new("vconsole", response.data)
                                        {
                                            clientUsername = indexed2.Username,
                                            clientAuthId = indexed2.AuthId
                                        };
                                        socket.Send(JsonConvert.SerializeObject(output5));
                                    }
                                }
                            }
                            break;
                        default:
                            Response output = new("status", "The server did not recognize a command: " + response.type.ToLower());
                            socket.Send(JsonConvert.SerializeObject(output));
                            IndexedClient? indexed = connections.Find(c => c.Session.ConnectionInfo.Id == socket.ConnectionInfo.Id);
                            if (indexed != null) connections.Remove(indexed);
                            socket.Close();
                            break;
                    }
                    PluginHandler.Handle(plugins, PluginHandleType.Server_PreGamemode_PostResponse, response, connections, socket, map, customizationOptions);
                    GamemodeHandler.Handle(gamemodeType, GamemodeHandleType.PostResponse, response, connections, socket, map, customizationOptions);
                    PluginHandler.Handle(plugins, PluginHandleType.Server_PostGamemode_PostResponse, response, connections, socket, map, customizationOptions);
                }
            }
        }
    }
}
