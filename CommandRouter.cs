using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using Newtonsoft.Json.Linq;
using ResoniteModLoader;

namespace AntigravityBridge;

/// <summary>
/// Routes JSON commands to the appropriate handler.
/// All FrooxEngine operations are dispatched to the engine thread via RunSynchronously.
/// </summary>
internal class CommandRouter
{
    private readonly SlotTracker _tracker;

    // Component type lookup — short name → FrooxEngine type
    private static readonly Dictionary<string, Type> ComponentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // UIX Core
        ["Canvas"] = typeof(Canvas),
        ["Image"] = typeof(Image),
        ["Text"] = typeof(FrooxEngine.UIX.Text),
        ["Button"] = typeof(Button),
        ["Mask"] = typeof(Mask),
        ["RawImage"] = typeof(RawImage),
        ["TextField"] = typeof(FrooxEngine.UIX.TextField),
        ["Checkbox"] = typeof(FrooxEngine.UIX.Checkbox),

        // UIX Layout
        ["RectTransform"] = typeof(RectTransform),
        ["VerticalLayout"] = typeof(VerticalLayout),
        ["HorizontalLayout"] = typeof(HorizontalLayout),
        ["GridLayout"] = typeof(FrooxEngine.UIX.GridLayout),
        ["LayoutElement"] = typeof(LayoutElement),
        ["ContentSizeFitter"] = typeof(ContentSizeFitter),
        ["ScrollRect"] = typeof(ScrollRect),
        ["IgnoreLayout"] = typeof(FrooxEngine.UIX.IgnoreLayout),

        // Textures & Sprites
        ["StaticTexture2D"] = typeof(StaticTexture2D),
        ["SpriteProvider"] = typeof(SpriteProvider),

        // Materials
        ["UnlitMaterial"] = typeof(UnlitMaterial),
        ["PBS_Metallic"] = typeof(PBS_Metallic),
        ["PBS_Specular"] = typeof(PBS_Specular),

        // Meshes & Rendering
        ["BoxMesh"] = typeof(BoxMesh),
        ["QuadMesh"] = typeof(QuadMesh),
        ["SphereMesh"] = typeof(SphereMesh),
        ["CylinderMesh"] = typeof(CylinderMesh),
        ["ConeMesh"] = typeof(ConeMesh),
        ["StaticMesh"] = typeof(StaticMesh),
        ["MeshRenderer"] = typeof(MeshRenderer),
        ["SkinnedMeshRenderer"] = typeof(SkinnedMeshRenderer),
        ["TextRenderer"] = typeof(TextRenderer),

        // Lighting
        ["Light"] = typeof(Light),

        // Colliders
        ["BoxCollider"] = typeof(BoxCollider),
        ["SphereCollider"] = typeof(SphereCollider),
        ["CapsuleCollider"] = typeof(CapsuleCollider),
        ["MeshCollider"] = typeof(MeshCollider),

        // Audio
        ["AudioClipPlayer"] = typeof(AudioClipPlayer),
        ["AudioOutput"] = typeof(AudioOutput),

        // Interaction
        ["Grabbable"] = typeof(Grabbable),

        // Animation / Motion
        ["Spinner"] = typeof(Spinner),
        ["Wiggler"] = typeof(Wiggler),
        ["Panner1D"] = typeof(Panner1D),
        ["Panner2D"] = typeof(Panner2D),

        // Dynamic Variables
        ["DynamicVariableSpace"] = typeof(DynamicVariableSpace),

        // Utility
        ["SmoothTransform"] = typeof(SmoothTransform),
        ["Comment"] = typeof(Comment),
    };

    public CommandRouter(SlotTracker tracker)
    {
        _tracker = tracker;
    }

    public int TrackedSlotCount => _tracker.Count;

    public JObject GetTrackedSlots() => _tracker.GetAllAsJson();

    /// <summary>
    /// Returns a JSON description of all available commands for the /help endpoint.
    /// </summary>
    public JObject GetCommandHelp()
    {
        return new JObject
        {
            ["commands"] = new JObject
            {
                ["ping"] = new JObject { ["params"] = "none", ["description"] = "Health check, returns pong" },
                ["createSlot"] = new JObject { ["params"] = "name, parent?, tag?, active?, position?, rotation?, scale?, components?[]", ["description"] = "Create slot with optional transform, tag, and inline components+fields" },
                ["setSlotActive"] = new JObject { ["params"] = "slot, active", ["description"] = "Enable/disable a slot" },
                ["setSlotTransform"] = new JObject { ["params"] = "slot, position?, rotation?, scale?", ["description"] = "Set local transform. Rotation: [euler] or [quaternion]. Scale: [x,y,z] or [uniform]" },
                ["setSlotName"] = new JObject { ["params"] = "slot, newName, updateTracker?", ["description"] = "Rename a slot, optionally re-key in tracker" },
                ["setSlotTag"] = new JObject { ["params"] = "slot, tag", ["description"] = "Set a slot's tag" },
                ["setSlotOrderIndex"] = new JObject { ["params"] = "slot, index", ["description"] = "Set a slot's ordering index among siblings (for UIX layout)" },
                ["destroySlot"] = new JObject { ["params"] = "slot", ["description"] = "Destroy a slot and purge tracker" },
                ["destroyChildren"] = new JObject { ["params"] = "slot", ["description"] = "Destroy children and purge tracker" },
                ["reparentSlot"] = new JObject { ["params"] = "slot, newParent, preserveGlobalTransform?", ["description"] = "Move slot under a new parent" },
                ["findSlot"] = new JObject { ["params"] = "name?, tag?, searchRoot?, trackAs?, matchSubstring?, ignoreCase?, maxDepth?", ["description"] = "Search scene graph by name/tag" },
                ["duplicateSlot"] = new JObject { ["params"] = "slot, trackAs?, keepGlobalTransform?", ["description"] = "Deep clone a slot hierarchy" },
                ["getSlotInfo"] = new JObject { ["params"] = "slot", ["description"] = "Inspect slot children and components" },
                ["attachComponent"] = new JObject { ["params"] = "slot, type", ["description"] = "Attach a component by type name" },
                ["removeComponent"] = new JObject { ["params"] = "slot, type", ["description"] = "Remove first component of type" },
                ["setField"] = new JObject { ["params"] = "slot, component, field, value", ["description"] = "Write a single field value" },
                ["setFields"] = new JObject { ["params"] = "slot, component, fields{}", ["description"] = "Write multiple fields at once: {\"fieldName\": value, ...}" },
                ["getComponentField"] = new JObject { ["params"] = "slot, component, field", ["description"] = "Read a single field value" },
                ["getComponentFields"] = new JObject { ["params"] = "slot, component", ["description"] = "List all fields on a component with types and values" },
                ["getSlotTransform"] = new JObject { ["params"] = "slot", ["description"] = "Read local and global transform" },
                ["listChildren"] = new JObject { ["params"] = "slot, depth?, trackAll?", ["description"] = "List children recursively with optional depth limit and auto-tracking" },
                ["getSlotsByTag"] = new JObject { ["params"] = "slot, tag, trackAll?", ["description"] = "Find all descendant slots matching a tag" },
                ["createDynVarSpace"] = new JObject { ["params"] = "slot, spaceName", ["description"] = "Create a DynamicVariableSpace" },
                ["createDynVar"] = new JObject { ["params"] = "slot, name, type, value?", ["description"] = "Create a dynamic variable" },
                ["readDynVar"] = new JObject { ["params"] = "slot, path, type?", ["description"] = "Read a dynamic variable value by path" },
                ["writeDynVar"] = new JObject { ["params"] = "slot, path, type, value", ["description"] = "Write a dynamic variable value by path" },
                ["importTexture"] = new JObject { ["params"] = "url, parent?, trackAs?, createSprite?", ["description"] = "Import texture from URL with optional SpriteProvider" },
                ["importMesh"] = new JObject { ["params"] = "url, parent?, trackAs?", ["description"] = "Import mesh from URL (creates StaticMesh)" },
                ["createPrimitive"] = new JObject { ["params"] = "name, parent?, meshType?, meshUrl?, material?, color?, position?, rotation?, scale?", ["description"] = "Create visible 3D object with mesh+renderer+material in one call" },
                ["clearTracker"] = new JObject { ["params"] = "none", ["description"] = "Clear all name→slot mappings" },
                ["trackExistingSlot"] = new JObject { ["params"] = "path, from?, trackAs?", ["description"] = "Find an existing slot by hierarchy path and register it in tracker" },
                ["buildUIXTree"] = new JObject { ["params"] = "parent?, root{}", ["description"] = "Build entire UI hierarchy from a declarative JSON tree" },
            },
            ["endpoints"] = new JObject
            {
                ["/ping"] = "GET — health check",
                ["/cmd"] = "POST — execute single command",
                ["/batch"] = "POST — execute batch (single engine dispatch)",
                ["/tracker"] = "GET — list tracked slots",
                ["/help"] = "GET — this help"
            },
            ["fieldTypes"] = new JArray("string", "bool", "int", "float", "float2", "float3", "float4", "floatQ", "colorX", "Uri", "enum (auto)", "SyncRef (auto)"),
            ["registeredComponents"] = new JArray(ComponentTypes.Keys.OrderBy(k => k).ToArray()),
            ["specialSlots"] = new JArray("__root__", "__worldroot__", "__localuser__"),
            ["apiVersion"] = 1
        };
    }

    /// <summary>
    /// Execute the action logic for a command. Must be called on the engine thread.
    /// </summary>
    private JObject ExecuteAction(string id, string action, JObject p)
    {
        return action switch
        {
            "ping" => Ok(id, new JObject { ["message"] = "pong" }),
            "log" => HandleLog(id, p),
            "createslot" => HandleCreateSlot(id, p),
            "setslotactive" => HandleSetSlotActive(id, p),
            "destroyslot" => HandleDestroySlot(id, p),
            "destroychildren" => HandleDestroyChildren(id, p),
            "attachcomponent" => HandleAttachComponent(id, p),
            "setfield" => HandleSetField(id, p),
            "setfields" => HandleSetFields(id, p),
            "createdynvarspace" => HandleCreateDynVarSpace(id, p),
            "createdynvar" => HandleCreateDynVar(id, p),
            "readdynvar" => HandleReadDynVar(id, p),
            "writedynvar" => HandleWriteDynVar(id, p),
            "getslotinfo" => HandleGetSlotInfo(id, p),
            "setslottransform" => HandleSetSlotTransform(id, p),
            "getcomponentfield" => HandleGetComponentField(id, p),
            "removecomponent" => HandleRemoveComponent(id, p),
            "reparentslot" => HandleReparentSlot(id, p),
            "setslotname" => HandleSetSlotName(id, p),
            "findslot" => HandleFindSlot(id, p),
            "duplicateslot" => HandleDuplicateSlot(id, p),
            "importtexture" => HandleImportTexture(id, p),
            "importmesh" => HandleImportMesh(id, p),
            "createprimitive" => HandleCreatePrimitive(id, p),
            "setslottag" => HandleSetSlotTag(id, p),
            "getcomponentfields" => HandleGetComponentFields(id, p),
            "getslottransform" => HandleGetSlotTransform(id, p),
            "listchildren" => HandleListChildren(id, p),
            "getslotsbytag" => HandleGetSlotsByTag(id, p),
            "setslotorderindex" => HandleSetSlotOrderIndex(id, p),
            "trackexistingslot" => HandleTrackExistingSlot(id, p),
            "builduixtree" => HandleBuildUIXTree(id, p),
            "cleartracker" => HandleClearTracker(id),
            _ => Error(id, $"Unknown action: {action}")
        };
    }

    /// <summary>
    /// Required parameters per action. Validated BEFORE dispatching to engine thread.
    /// </summary>
    private static readonly Dictionary<string, string[]> RequiredParams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["createSlot"] = new[] { "name" },
        ["setSlotActive"] = new[] { "slot" },
        ["destroySlot"] = new[] { "slot" },
        ["destroyChildren"] = new[] { "slot" },
        ["attachComponent"] = new[] { "slot", "type" },
        ["setField"] = new[] { "slot", "component", "field" },
        ["setFields"] = new[] { "slot", "component", "fields" },
        ["createDynVarSpace"] = new[] { "slot", "spaceName" },
        ["createDynVar"] = new[] { "slot", "varName" },
        ["readDynVar"] = new[] { "slot", "path" },
        ["writeDynVar"] = new[] { "slot", "path", "value" },
        ["getSlotInfo"] = new[] { "slot" },
        ["setSlotTransform"] = new[] { "slot" },
        ["getComponentField"] = new[] { "slot", "component", "field" },
        ["removeComponent"] = new[] { "slot", "type" },
        ["reparentSlot"] = new[] { "slot", "newParent" },
        ["setSlotName"] = new[] { "slot", "newName" },
        ["findSlot"] = Array.Empty<string>(),
        ["duplicateSlot"] = new[] { "slot" },
        ["importTexture"] = new[] { "url" },
        ["importMesh"] = new[] { "url" },
        ["createPrimitive"] = Array.Empty<string>(),
        ["setSlotTag"] = new[] { "slot" },
        ["setSlotOrderIndex"] = new[] { "slot" },
        ["getComponentFields"] = new[] { "slot", "component" },
        ["getSlotTransform"] = new[] { "slot" },
        ["listChildren"] = new[] { "slot" },
        ["getSlotsByTag"] = new[] { "tag" },
        ["trackExistingSlot"] = new[] { "path" },
        ["buildUIXTree"] = new[] { "root" },
    };

    /// <summary>
    /// Validate required params before dispatching to engine thread.
    /// Returns null if valid, or an error JObject if invalid.
    /// </summary>
    private JObject ValidateParams(string id, string action, JObject p)
    {
        if (!RequiredParams.TryGetValue(action, out var required))
            return null; // Unknown action or no validation — let handler deal with it

        foreach (var param in required)
        {
            if (p[param] == null)
                return Error(id, $"Missing required parameter '{param}' for action '{action}'");
        }
        return null;
    }

    /// <summary>
    /// Execute a single command JSON object. Dispatches to the engine thread.
    /// </summary>
    public JObject ExecuteCommand(JObject cmd)
    {
        string id = cmd["id"]?.ToString() ?? "";
        string action = cmd["action"]?.ToString()?.ToLowerInvariant();
        var p = cmd["params"] as JObject ?? new JObject();

        if (string.IsNullOrEmpty(action))
            return Error(id, "Missing 'action' field");

        // Schema validation — fast fail before engine dispatch
        var validationError = ValidateParams(id, action, p);
        if (validationError != null)
            return validationError;

        var tcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Engine.Current.WorldManager.FocusedWorld.RunSynchronously(() =>
            {
                try
                {
                    tcs.SetResult(ExecuteAction(id, action, p));
                }
                catch (Exception ex)
                {
                    tcs.SetResult(Error(id, $"Engine error: {ex.Message}"));
                    ResoniteMod.Error($"[CMD {id}] {ex}");
                }
            });

            if (!tcs.Task.Wait(TimeSpan.FromSeconds(10)))
                return Error(id, "Command timed out (10s) waiting for engine thread");
        }
        catch (Exception ex)
        {
            return Error(id, $"Dispatch error: {ex.Message}");
        }

        return tcs.Task.Result ?? Error(id, "No result returned");
    }

    /// <summary>
    /// Execute a batch of commands in a single engine thread dispatch.
    /// Dramatically faster than calling ExecuteCommand per command.
    /// </summary>
    public JObject ExecuteBatch(JArray commands, bool stopOnError)
    {
        var results = new JArray();
        int successCount = 0;
        int errorCount = 0;
        int? stoppedAtIndex = null;

        // Pre-validate all commands before engine dispatch
        for (int i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i] as JObject;
            if (cmd == null) continue;

            string id = cmd["id"]?.ToString() ?? "";
            string action = cmd["action"]?.ToString()?.ToLowerInvariant();
            var p = cmd["params"] as JObject ?? new JObject();

            if (!string.IsNullOrEmpty(action))
            {
                var validationError = ValidateParams(id, action, p);
                if (validationError != null)
                {
                    results.Add(validationError);
                    errorCount++;
                    if (stopOnError)
                    {
                        stoppedAtIndex = i;
                        return new JObject
                        {
                            ["status"] = "stopped",
                            ["successCount"] = successCount,
                            ["errorCount"] = errorCount,
                            ["stoppedAtIndex"] = stoppedAtIndex,
                            ["results"] = results
                        };
                    }
                    // Mark this command as already handled
                    cmd["__validated_error"] = true;
                }
            }
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Engine.Current.WorldManager.FocusedWorld.RunSynchronously(() =>
            {
                try
                {
                    for (int i = 0; i < commands.Count; i++)
                    {
                        var cmd = commands[i] as JObject;
                        if (cmd == null) continue;

                        // Skip commands that already failed validation
                        if (cmd["__validated_error"]?.Value<bool>() == true)
                            continue;

                        string id = cmd["id"]?.ToString() ?? "";
                        string action = cmd["action"]?.ToString()?.ToLowerInvariant();
                        var p = cmd["params"] as JObject ?? new JObject();

                        JObject result;
                        if (string.IsNullOrEmpty(action))
                        {
                            result = Error(id, "Missing 'action' field");
                        }
                        else
                        {
                            try
                            {
                                result = ExecuteAction(id, action, p);
                            }
                            catch (Exception ex)
                            {
                                result = Error(id, $"Engine error: {ex.Message}");
                                ResoniteMod.Error($"[CMD {id}] {ex}");
                            }
                        }

                        results.Add(result);

                        if (result["status"]?.ToString() == "ok")
                            successCount++;
                        else
                        {
                            errorCount++;
                            if (stopOnError)
                            {
                                stoppedAtIndex = i;
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    tcs.SetResult(true);
                }
            });

            // Scale timeout with batch size (min 30s, ~0.5s per command)
            int timeoutSeconds = Math.Max(30, commands.Count / 2);
            if (!tcs.Task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                return new JObject
                {
                    ["status"] = "error",
                    ["error"] = $"Batch timed out ({timeoutSeconds}s) waiting for engine thread",
                    ["executed"] = successCount + errorCount,
                    ["results"] = results
                };
            }
        }
        catch (Exception ex)
        {
            return new JObject
            {
                ["status"] = "error",
                ["error"] = $"Dispatch error: {ex.Message}"
            };
        }

        // Clean up validation markers
        foreach (var cmd in commands)
            (cmd as JObject)?.Remove("__validated_error");

        var responseBody = new JObject
        {
            ["status"] = errorCount == 0 ? "ok" : (stoppedAtIndex.HasValue ? "stopped" : "partial"),
            ["total"] = commands.Count,
            ["executed"] = successCount + errorCount,
            ["success"] = successCount,
            ["errors"] = errorCount,
            ["results"] = results
        };

        if (stoppedAtIndex.HasValue)
            responseBody["stoppedAtIndex"] = stoppedAtIndex.Value;

        return responseBody;
    }

    // ─── Handlers ───────────────────────────────────────────────

    private JObject HandleLog(string id, JObject p)
    {
        string message = p["message"]?.ToString() ?? "";
        string level = p["level"]?.ToString()?.ToLowerInvariant() ?? "info";

        switch (level)
        {
            case "warn":
                ResoniteMod.Warn($"[Bridge] {message}");
                break;
            case "error":
                ResoniteMod.Error($"[Bridge] {message}");
                break;
            default:
                ResoniteMod.Msg($"[Bridge] {message}");
                break;
        }

        return Ok(id, new JObject { ["logged"] = true, ["level"] = level });
    }

    private JObject HandleCreateSlot(string id, JObject p)
    {
        string name = p["name"]?.ToString();
        string parentName = p["parent"]?.ToString() ?? "__root__";
        string tag = p["tag"]?.ToString();
        bool active = p["active"]?.Value<bool>() ?? true;

        if (string.IsNullOrEmpty(name))
            return Error(id, "createSlot requires 'name'");

        var parent = _tracker.Get(parentName);
        if (parent == null)
            return Error(id, $"Parent slot '{parentName}' not found");

        var slot = parent.AddSlot(name);
        if (!string.IsNullOrEmpty(tag))
            slot.Tag = tag;
        slot.ActiveSelf = active;

        // Optional transform
        var pos = p["position"] as JArray;
        if (pos != null && pos.Count == 3)
            slot.LocalPosition = new float3(pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());

        var rot = p["rotation"] as JArray;
        if (rot != null && rot.Count == 3)
            slot.LocalRotation = floatQ.Euler(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>());
        else if (rot != null && rot.Count == 4)
            slot.LocalRotation = new floatQ(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>(), rot[3].Value<float>());

        var scale = p["scale"] as JArray;
        if (scale != null && scale.Count == 3)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[1].Value<float>(), scale[2].Value<float>());
        else if (scale != null && scale.Count == 1)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[0].Value<float>(), scale[0].Value<float>());

        _tracker.Register(name, slot);

        var result = new JObject
        {
            ["slotName"] = name,
            ["refId"] = slot.ReferenceID.ToString()
        };

        // Optional inline component attachment
        var componentsArr = p["components"] as JArray;
        if (componentsArr != null && componentsArr.Count > 0)
        {
            var attachedComponents = new JArray();
            var compErrors = new JArray();

            foreach (JObject compDef in componentsArr)
            {
                string typeName = compDef["type"]?.ToString();
                var fields = compDef["fields"] as JObject;

                Type componentType = ResolveComponentType(typeName);
                if (componentType == null)
                {
                    compErrors.Add(new JObject { ["type"] = typeName, ["error"] = $"Unknown component type '{typeName}'" });
                    continue;
                }

                try
                {
                    var component = slot.AttachComponent(componentType);
                    var compResult = new JObject
                    {
                        ["type"] = componentType.Name,
                        ["refId"] = component.ReferenceID.ToString()
                    };

                    // Set fields if provided
                    if (fields != null && fields.Count > 0)
                    {
                        var setFieldNames = new JArray();
                        var fieldErrors = new JArray();

                        foreach (var kvp in fields)
                        {
                            try
                            {
                                SetFieldValue(component, kvp.Key, kvp.Value);
                                setFieldNames.Add(kvp.Key);
                            }
                            catch (Exception ex)
                            {
                                fieldErrors.Add(new JObject { ["field"] = kvp.Key, ["error"] = ex.Message });
                            }
                        }

                        compResult["fieldsSet"] = setFieldNames;
                        if (fieldErrors.Count > 0)
                            compResult["fieldErrors"] = fieldErrors;
                    }

                    attachedComponents.Add(compResult);
                }
                catch (Exception ex)
                {
                    compErrors.Add(new JObject { ["type"] = typeName, ["error"] = ex.Message });
                }
            }

            result["components"] = attachedComponents;
            if (compErrors.Count > 0)
                result["componentErrors"] = compErrors;
        }

        return Ok(id, result);
    }

    private JObject HandleSetSlotActive(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        bool active = p["active"]?.Value<bool>() ?? true;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        slot.ActiveSelf = active;

        return Ok(id, new JObject { ["slot"] = slotName, ["active"] = active });
    }

    private JObject HandleDestroySlot(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        slot.Destroy();
        _tracker.Unregister(slotName);
        int purged = _tracker.PurgeDestroyed();

        return Ok(id, new JObject
        {
            ["destroyed"] = slotName,
            ["trackerEntriesPurged"] = purged
        });
    }

    private JObject HandleDestroyChildren(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        slot.DestroyChildren();
        int purged = _tracker.PurgeDestroyed();

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["childrenDestroyed"] = true,
            ["trackerEntriesPurged"] = purged
        });
    }

    private JObject HandleAttachComponent(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string typeName = p["type"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        Type componentType = ResolveComponentType(typeName);
        if (componentType == null)
            return Error(id, $"Component type '{typeName}' not found");

        var component = slot.AttachComponent(componentType);

        // Apply initial field values if provided
        var fields = p["fields"] as JObject;
        if (fields != null)
        {
            foreach (var kvp in fields)
            {
                try
                {
                    SetFieldValue(component, kvp.Key, kvp.Value);
                }
                catch (Exception ex)
                {
                    ResoniteMod.Warn($"[CMD {id}] Failed to set field '{kvp.Key}': {ex.Message}");
                }
            }
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["component"] = componentType.Name,
            ["refId"] = component.ReferenceID.ToString()
        });
    }

    private JObject HandleSetField(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        string fieldName = p["field"]?.ToString();
        var value = p["value"];

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        Type componentType = ResolveComponentType(componentName);
        if (componentType == null)
            return Error(id, $"Component type '{componentName}' not found");

        var component = slot.GetComponent(componentType);
        if (component == null)
            return Error(id, $"Component '{componentName}' not found on slot '{slotName}'");

        try
        {
            SetFieldValue(component, fieldName, value);
        }
        catch (Exception ex)
        {
            return Error(id, $"Failed to set {componentName}.{fieldName}: {ex.Message}");
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["component"] = componentName,
            ["field"] = fieldName,
            ["set"] = true
        });
    }

    private JObject HandleSetFields(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        var fields = p["fields"] as JObject;

        if (fields == null || fields.Count == 0)
            return Error(id, "setFields requires 'fields' object with field→value pairs");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        Type componentType = ResolveComponentType(componentName);
        if (componentType == null)
            return Error(id, $"Component type '{componentName}' not found");

        var component = slot.GetComponent(componentType);
        if (component == null)
            return Error(id, $"Component '{componentName}' not found on slot '{slotName}'");

        var setFields = new JArray();
        var errors = new JArray();

        foreach (var kvp in fields)
        {
            try
            {
                SetFieldValue(component, kvp.Key, kvp.Value);
                setFields.Add(kvp.Key);
            }
            catch (Exception ex)
            {
                errors.Add(new JObject
                {
                    ["field"] = kvp.Key,
                    ["error"] = ex.Message
                });
            }
        }

        var result = new JObject
        {
            ["slot"] = slotName,
            ["component"] = componentName,
            ["set"] = setFields,
            ["setCount"] = setFields.Count,
            ["totalRequested"] = fields.Count
        };

        if (errors.Count > 0)
            result["errors"] = errors;

        return Ok(id, result);
    }

    private JObject HandleCreateDynVarSpace(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string spaceName = p["spaceName"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        var space = slot.AttachComponent<DynamicVariableSpace>();
        space.SpaceName.Value = spaceName;

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["spaceName"] = spaceName,
            ["refId"] = space.ReferenceID.ToString()
        });
    }

    private JObject HandleCreateDynVar(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string varName = p["varName"]?.ToString();
        string varType = p["varType"]?.ToString()?.ToLowerInvariant() ?? "string";
        var initialValue = p["value"];

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        string refId;
        switch (varType)
        {
            case "string":
                var sv = slot.AttachComponent<DynamicValueVariable<string>>();
                sv.VariableName.Value = varName;
                if (initialValue != null) sv.Value.Value = initialValue.ToString();
                refId = sv.ReferenceID.ToString();
                break;
            case "bool":
                var bv = slot.AttachComponent<DynamicValueVariable<bool>>();
                bv.VariableName.Value = varName;
                if (initialValue != null) bv.Value.Value = initialValue.Value<bool>();
                refId = bv.ReferenceID.ToString();
                break;
            case "int":
                var iv = slot.AttachComponent<DynamicValueVariable<int>>();
                iv.VariableName.Value = varName;
                if (initialValue != null) iv.Value.Value = initialValue.Value<int>();
                refId = iv.ReferenceID.ToString();
                break;
            case "float":
                var fv = slot.AttachComponent<DynamicValueVariable<float>>();
                fv.VariableName.Value = varName;
                if (initialValue != null) fv.Value.Value = initialValue.Value<float>();
                refId = fv.ReferenceID.ToString();
                break;
            case "float3":
            {
                var f3v = slot.AttachComponent<DynamicValueVariable<float3>>();
                f3v.VariableName.Value = varName;
                if (initialValue is JArray f3a && f3a.Count == 3)
                    f3v.Value.Value = new float3(f3a[0].Value<float>(), f3a[1].Value<float>(), f3a[2].Value<float>());
                refId = f3v.ReferenceID.ToString();
                break;
            }
            case "colorx":
            {
                var cv = slot.AttachComponent<DynamicValueVariable<colorX>>();
                cv.VariableName.Value = varName;
                if (initialValue is JArray ca && ca.Count >= 3)
                {
                    float a = ca.Count >= 4 ? ca[3].Value<float>() : 1f;
                    cv.Value.Value = new colorX(ca[0].Value<float>(), ca[1].Value<float>(), ca[2].Value<float>(), a);
                }
                refId = cv.ReferenceID.ToString();
                break;
            }
            default:
                return Error(id, $"Unsupported DynVar type: {varType}. Use string, bool, int, float, float3, or colorX.");
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["varName"] = varName,
            ["varType"] = varType,
            ["refId"] = refId
        });
    }

    private JObject HandleReadDynVar(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string path = p["path"]?.ToString();
        string varType = p["type"]?.ToString()?.ToLowerInvariant() ?? "string";

        if (string.IsNullOrEmpty(path))
            return Error(id, "readDynVar requires 'path'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        // Parse path to find space and variable name
        DynamicVariableHelper.ParsePath(path, out string spaceName, out string variableName);
        var space = DynamicVariableHelper.FindSpace(slot, spaceName);
        if (space == null)
            return Error(id, $"DynamicVariableSpace '{spaceName}' not found from slot '{slotName}'");

        JToken value;
        bool found;
        switch (varType)
        {
            case "string":
                found = space.TryReadValue<string>(variableName, out var sv);
                value = found ? (JToken)(sv ?? "") : JValue.CreateNull();
                break;
            case "bool":
                found = space.TryReadValue<bool>(variableName, out var bv);
                value = found ? (JToken)bv : JValue.CreateNull();
                break;
            case "int":
                found = space.TryReadValue<int>(variableName, out var iv);
                value = found ? (JToken)iv : JValue.CreateNull();
                break;
            case "float":
                found = space.TryReadValue<float>(variableName, out var fv);
                value = found ? (JToken)fv : JValue.CreateNull();
                break;
            case "float3":
                found = space.TryReadValue<float3>(variableName, out var f3v);
                value = found ? new JArray(f3v.x, f3v.y, f3v.z) : JValue.CreateNull();
                break;
            case "colorx":
                found = space.TryReadValue<colorX>(variableName, out var cv);
                value = found ? new JArray(cv.r, cv.g, cv.b, cv.a) : JValue.CreateNull();
                break;
            default:
                return Error(id, $"Unsupported DynVar read type: {varType}. Use string, bool, int, float, float3, or colorX.");
        }

        if (!found)
            return Error(id, $"Dynamic variable '{path}' not found or type mismatch");

        return Ok(id, new JObject
        {
            ["path"] = path,
            ["type"] = varType,
            ["value"] = value
        });
    }

    private JObject HandleWriteDynVar(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string path = p["path"]?.ToString();
        string varType = p["type"]?.ToString()?.ToLowerInvariant() ?? "string";
        var val = p["value"];

        if (string.IsNullOrEmpty(path))
            return Error(id, "writeDynVar requires 'path'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        switch (varType)
        {
            case "string":
                DynamicVariableHelper.WriteDynamicVariable(slot, path, val?.ToString() ?? "");
                break;
            case "bool":
                DynamicVariableHelper.WriteDynamicVariable(slot, path, val?.Value<bool>() ?? false);
                break;
            case "int":
                DynamicVariableHelper.WriteDynamicVariable(slot, path, val?.Value<int>() ?? 0);
                break;
            case "float":
                DynamicVariableHelper.WriteDynamicVariable(slot, path, val?.Value<float>() ?? 0f);
                break;
            case "float3":
            {
                var arr = val as JArray;
                if (arr == null || arr.Count != 3)
                    return Error(id, "float3 requires [x, y, z] array");
                DynamicVariableHelper.WriteDynamicVariable(slot, path,
                    new float3(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>()));
                break;
            }
            case "colorx":
            {
                var arr = val as JArray;
                if (arr == null || arr.Count < 3)
                    return Error(id, "colorX requires [r, g, b, a] array");
                float a = arr.Count >= 4 ? arr[3].Value<float>() : 1f;
                DynamicVariableHelper.WriteDynamicVariable(slot, path,
                    new colorX(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>(), a));
                break;
            }
            default:
                return Error(id, $"Unsupported DynVar write type: {varType}. Use string, bool, int, float, float3, or colorX.");
        }

        return Ok(id, new JObject
        {
            ["path"] = path,
            ["type"] = varType,
            ["written"] = true
        });
    }

    private JObject HandleGetSlotInfo(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        var children = new JArray();
        foreach (var child in slot.Children)
        {
            children.Add(new JObject
            {
                ["name"] = child.Name,
                ["refId"] = child.ReferenceID.ToString(),
                ["active"] = child.ActiveSelf,
                ["childCount"] = child.ChildrenCount,
                ["componentCount"] = child.ComponentCount
            });
        }

        var components = new JArray();
        foreach (var comp in slot.Components)
        {
            components.Add(new JObject
            {
                ["type"] = comp.GetType().Name,
                ["refId"] = comp.ReferenceID.ToString()
            });
        }

        return Ok(id, new JObject
        {
            ["name"] = slot.Name,
            ["refId"] = slot.ReferenceID.ToString(),
            ["active"] = slot.ActiveSelf,
            ["tag"] = slot.Tag,
            ["childCount"] = slot.ChildrenCount,
            ["children"] = children,
            ["components"] = components
        });
    }

    private JObject HandleSetSlotTransform(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        var pos = p["position"] as JArray;
        if (pos != null && pos.Count == 3)
            slot.LocalPosition = new float3(pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());

        var rot = p["rotation"] as JArray;
        if (rot != null && rot.Count == 3)
            slot.LocalRotation = floatQ.Euler(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>());
        else if (rot != null && rot.Count == 4)
            slot.LocalRotation = new floatQ(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>(), rot[3].Value<float>());

        var scale = p["scale"] as JArray;
        if (scale != null && scale.Count == 3)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[1].Value<float>(), scale[2].Value<float>());
        else if (scale != null && scale.Count == 1)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[0].Value<float>(), scale[0].Value<float>());

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["position"] = new JArray(slot.LocalPosition.x, slot.LocalPosition.y, slot.LocalPosition.z),
            ["rotation"] = new JArray(slot.LocalRotation.x, slot.LocalRotation.y, slot.LocalRotation.z, slot.LocalRotation.w),
            ["scale"] = new JArray(slot.LocalScale.x, slot.LocalScale.y, slot.LocalScale.z)
        });
    }

    private JObject HandleSetSlotTag(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string tag = p["tag"]?.ToString() ?? "";

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        slot.Tag = tag;

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["tag"] = tag
        });
    }

    private JObject HandleSetSlotOrderIndex(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        int index = p["index"]?.Value<int>() ?? 0;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        slot.ChildIndex = index;

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["index"] = slot.ChildIndex
        });
    }

    private JObject HandleClearTracker(string id)
    {
        _tracker.Clear();
        return Ok(id, new JObject { ["cleared"] = true });
    }

    private JObject HandleTrackExistingSlot(string id, JObject p)
    {
        string path = p["path"]?.ToString();
        string fromSlot = p["from"]?.ToString() ?? "__root__";
        string trackAs = p["trackAs"]?.ToString();

        if (string.IsNullOrEmpty(path))
            return Error(id, "trackExistingSlot requires 'path'");

        var start = _tracker.Get(fromSlot);
        if (start == null)
            return Error(id, $"Starting slot '{fromSlot}' not found");

        // Navigate the path: "Child1/Child2/Target"
        var segments = path.Split('/');
        Slot current = start;
        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i].Trim();
            if (string.IsNullOrEmpty(seg)) continue;

            var next = current.FindChild(seg);
            if (next == null)
            {
                string traversed = string.Join("/", segments.Take(i));
                return Error(id, $"Child '{seg}' not found under '{(traversed.Length > 0 ? traversed : fromSlot)}'");
            }
            current = next;
        }

        string name = trackAs ?? current.Name;
        _tracker.Register(name, current);

        return Ok(id, new JObject
        {
            ["trackedAs"] = name,
            ["name"] = current.Name,
            ["refId"] = current.ReferenceID.ToString(),
            ["path"] = path,
            ["active"] = current.ActiveSelf,
            ["childCount"] = current.ChildrenCount,
            ["componentCount"] = current.ComponentCount
        });
    }

    private JObject HandleBuildUIXTree(string id, JObject p)
    {
        string parentName = p["parent"]?.ToString() ?? "__root__";
        var root = p["root"] as JObject;

        if (root == null)
            return Error(id, "buildUIXTree requires 'root' object");

        var parent = _tracker.Get(parentName);
        if (parent == null)
            return Error(id, $"Parent slot '{parentName}' not found");

        var created = new JArray();
        var errors = new JArray();

        BuildTreeNode(parent, root, created, errors);

        return Ok(id, new JObject
        {
            ["slotsCreated"] = created.Count,
            ["errors"] = errors.Count,
            ["slots"] = created,
            ["errorDetails"] = errors
        });
    }

    private void BuildTreeNode(Slot parent, JObject node, JArray created, JArray errors)
    {
        string name = node["name"]?.ToString() ?? "Node";
        string tag = node["tag"]?.ToString();
        bool active = node["active"]?.Value<bool>() ?? true;

        var slot = parent.AddSlot(name);
        if (!string.IsNullOrEmpty(tag))
            slot.Tag = tag;
        slot.ActiveSelf = active;

        // Transform
        var pos = node["position"] as JArray;
        if (pos != null && pos.Count == 3)
            slot.LocalPosition = new float3(pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());

        var rot = node["rotation"] as JArray;
        if (rot != null && rot.Count == 3)
            slot.LocalRotation = floatQ.Euler(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>());
        else if (rot != null && rot.Count == 4)
            slot.LocalRotation = new floatQ(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>(), rot[3].Value<float>());

        var scale = node["scale"] as JArray;
        if (scale != null && scale.Count == 3)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[1].Value<float>(), scale[2].Value<float>());
        else if (scale != null && scale.Count == 1)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[0].Value<float>(), scale[0].Value<float>());

        _tracker.Register(name, slot);

        var slotResult = new JObject
        {
            ["name"] = name,
            ["refId"] = slot.ReferenceID.ToString()
        };

        // Components
        var componentsArr = node["components"] as JArray;
        if (componentsArr != null)
        {
            var attachedComps = new JArray();
            foreach (JObject compDef in componentsArr)
            {
                string typeName = compDef["type"]?.ToString();
                var fields = compDef["fields"] as JObject;

                Type componentType = ResolveComponentType(typeName);
                if (componentType == null)
                {
                    errors.Add(new JObject { ["slot"] = name, ["error"] = $"Unknown component type '{typeName}'" });
                    continue;
                }

                try
                {
                    var component = slot.AttachComponent(componentType);

                    if (fields != null)
                    {
                        foreach (var kvp in fields)
                        {
                            try
                            {
                                SetFieldValue(component, kvp.Key, kvp.Value);
                            }
                            catch (Exception ex)
                            {
                                errors.Add(new JObject { ["slot"] = name, ["component"] = typeName, ["field"] = kvp.Key, ["error"] = ex.Message });
                            }
                        }
                    }

                    attachedComps.Add(componentType.Name);
                }
                catch (Exception ex)
                {
                    errors.Add(new JObject { ["slot"] = name, ["error"] = $"Failed to attach {typeName}: {ex.Message}" });
                }
            }

            if (attachedComps.Count > 0)
                slotResult["components"] = attachedComps;
        }

        created.Add(slotResult);

        // Recurse into children
        var children = node["children"] as JArray;
        if (children != null)
        {
            foreach (JObject child in children)
            {
                BuildTreeNode(slot, child, created, errors);
            }
        }
    }

    private JObject HandleRemoveComponent(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string typeName = p["type"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        Type componentType = ResolveComponentType(typeName);
        if (componentType == null)
            return Error(id, $"Component type '{typeName}' not found");

        var component = slot.GetComponent(componentType);
        if (component == null)
            return Error(id, $"Component '{typeName}' not found on slot '{slotName}'");

        component.Destroy();

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["removedComponent"] = typeName
        });
    }

    private JObject HandleReparentSlot(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string newParentName = p["newParent"]?.ToString();
        bool preserveGlobal = p["preserveGlobalTransform"]?.Value<bool>() ?? false;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        if (string.IsNullOrEmpty(newParentName))
            return Error(id, "reparentSlot requires 'newParent'");

        var newParent = _tracker.Get(newParentName);
        if (newParent == null)
            return Error(id, $"New parent slot '{newParentName}' not found");

        slot.SetParent(newParent, preserveGlobal);

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["newParent"] = newParentName,
            ["preservedGlobalTransform"] = preserveGlobal
        });
    }

    private JObject HandleSetSlotName(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string newName = p["newName"]?.ToString();
        bool updateTracker = p["updateTracker"]?.Value<bool>() ?? true;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        if (string.IsNullOrEmpty(newName))
            return Error(id, "setSlotName requires 'newName'");

        slot.Name = newName;

        if (updateTracker)
        {
            _tracker.Unregister(slotName);
            _tracker.Register(newName, slot);
        }

        return Ok(id, new JObject
        {
            ["oldName"] = slotName,
            ["newName"] = newName,
            ["trackerUpdated"] = updateTracker
        });
    }

    private JObject HandleFindSlot(string id, JObject p)
    {
        string searchRoot = p["searchRoot"]?.ToString() ?? "__root__";
        string name = p["name"]?.ToString();
        string tag = p["tag"]?.ToString();
        string trackAs = p["trackAs"]?.ToString();
        bool matchSubstring = p["matchSubstring"]?.Value<bool>() ?? false;
        bool ignoreCase = p["ignoreCase"]?.Value<bool>() ?? true;
        int maxDepth = p["maxDepth"]?.Value<int>() ?? -1;

        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(tag))
            return Error(id, "findSlot requires 'name' or 'tag' (or both)");

        var root = _tracker.Get(searchRoot);
        if (root == null)
            return Error(id, $"Search root '{searchRoot}' not found");

        Slot found = null;

        if (!string.IsNullOrEmpty(name))
        {
            // Use the flexible FindChild overload with substring/case options
            found = root.FindChild(name, matchSubstring, ignoreCase, maxDepth);

            // If also filtering by tag, verify the tag matches
            if (found != null && !string.IsNullOrEmpty(tag) && found.Tag != tag)
                found = null;
        }
        else if (!string.IsNullOrEmpty(tag))
        {
            // Tag-only search: use GetChildrenWithTag and return first match
            var tagged = root.GetChildrenWithTag(tag);
            found = tagged.Count > 0 ? tagged[0] : null;
        }

        if (found == null)
        {
            string criteria = !string.IsNullOrEmpty(name) ? $"name='{name}'" : $"tag='{tag}'";
            return Error(id, $"No slot matching {criteria} found under '{searchRoot}'");
        }

        // Register in tracker
        string trackName = trackAs ?? found.Name;
        _tracker.Register(trackName, found);

        return Ok(id, new JObject
        {
            ["name"] = found.Name,
            ["refId"] = found.ReferenceID.ToString(),
            ["tag"] = found.Tag,
            ["trackedAs"] = trackName,
            ["active"] = found.ActiveSelf,
            ["childCount"] = found.ChildrenCount,
            ["componentCount"] = found.ComponentCount
        });
    }

    private JObject HandleDuplicateSlot(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string trackAs = p["trackAs"]?.ToString();
        bool keepGlobalTransform = p["keepGlobalTransform"]?.Value<bool>() ?? true;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        var duplicate = slot.Duplicate(keepGlobalTransform: keepGlobalTransform);

        // Register with provided name or auto-generated name
        string name = trackAs ?? $"{slot.Name}_copy";
        duplicate.Name = name;
        _tracker.Register(name, duplicate);

        return Ok(id, new JObject
        {
            ["originalSlot"] = slotName,
            ["duplicateName"] = name,
            ["refId"] = duplicate.ReferenceID.ToString(),
            ["trackedAs"] = name
        });
    }

    private JObject HandleImportTexture(string id, JObject p)
    {
        string url = p["url"]?.ToString();
        string parentName = p["parent"]?.ToString() ?? "__root__";
        string trackAs = p["trackAs"]?.ToString();
        bool createSprite = p["createSprite"]?.Value<bool>() ?? true;

        if (string.IsNullOrEmpty(url))
            return Error(id, "importTexture requires 'url'");

        var parent = _tracker.Get(parentName);
        if (parent == null)
            return Error(id, $"Parent slot '{parentName}' not found");

        // Create asset slot
        string slotName = trackAs ?? "ImportedTexture";
        var slot = parent.AddSlot(slotName);
        _tracker.Register(slotName, slot);

        // Attach StaticTexture2D and set URL
        var texture = slot.AttachComponent<StaticTexture2D>();
        var urlField = texture.GetSyncMember("URL") as Sync<Uri>;
        if (urlField != null)
            urlField.Value = new Uri(url);

        var result = new JObject
        {
            ["slot"] = slotName,
            ["refId"] = slot.ReferenceID.ToString(),
            ["textureRefId"] = texture.ReferenceID.ToString(),
            ["url"] = url,
            ["trackedAs"] = slotName
        };

        // Optionally create a SpriteProvider for UIX Image use
        if (createSprite)
        {
            var sprite = slot.AttachComponent<SpriteProvider>();
            // Wire the sprite's Texture to our StaticTexture2D
            var spriteTexRef = sprite.GetSyncMember("Texture") as ISyncRef;
            spriteTexRef?.TrySet(texture);

            result["spriteRefId"] = sprite.ReferenceID.ToString();
        }

        return Ok(id, result);
    }

    private JObject HandleImportMesh(string id, JObject p)
    {
        string url = p["url"]?.ToString();
        string parentName = p["parent"]?.ToString() ?? "__root__";
        string trackAs = p["trackAs"]?.ToString();

        if (string.IsNullOrEmpty(url))
            return Error(id, "importMesh requires 'url'");

        var parent = _tracker.Get(parentName);
        if (parent == null)
            return Error(id, $"Parent slot '{parentName}' not found");

        string slotName = trackAs ?? "ImportedMesh";
        var slot = parent.AddSlot(slotName);
        _tracker.Register(slotName, slot);

        var mesh = slot.AttachComponent<StaticMesh>();
        var urlField = mesh.GetSyncMember("URL") as Sync<Uri>;
        if (urlField != null)
            urlField.Value = new Uri(url);

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["refId"] = slot.ReferenceID.ToString(),
            ["meshRefId"] = mesh.ReferenceID.ToString(),
            ["url"] = url,
            ["trackedAs"] = slotName
        });
    }

    private JObject HandleCreatePrimitive(string id, JObject p)
    {
        string name = p["name"]?.ToString() ?? "Primitive";
        string parentName = p["parent"]?.ToString() ?? "__root__";
        string meshType = p["meshType"]?.ToString();
        string meshUrl = p["meshUrl"]?.ToString();
        string materialType = p["material"]?.ToString() ?? "PBS_Metallic";

        var parent = _tracker.Get(parentName);
        if (parent == null)
            return Error(id, $"Parent slot '{parentName}' not found");

        // Create main slot
        var slot = parent.AddSlot(name);
        _tracker.Register(name, slot);

        // Optional transform
        var pos = p["position"] as JArray;
        if (pos != null && pos.Count == 3)
            slot.LocalPosition = new float3(pos[0].Value<float>(), pos[1].Value<float>(), pos[2].Value<float>());

        var rot = p["rotation"] as JArray;
        if (rot != null && rot.Count == 3)
            slot.LocalRotation = floatQ.Euler(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>());
        else if (rot != null && rot.Count == 4)
            slot.LocalRotation = new floatQ(rot[0].Value<float>(), rot[1].Value<float>(), rot[2].Value<float>(), rot[3].Value<float>());

        var scale = p["scale"] as JArray;
        if (scale != null && scale.Count == 3)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[1].Value<float>(), scale[2].Value<float>());
        else if (scale != null && scale.Count == 1)
            slot.LocalScale = new float3(scale[0].Value<float>(), scale[0].Value<float>(), scale[0].Value<float>());

        var result = new JObject
        {
            ["slot"] = name,
            ["refId"] = slot.ReferenceID.ToString()
        };

        // Attach mesh
        Component meshComponent = null;
        if (!string.IsNullOrEmpty(meshUrl))
        {
            // Static mesh from URL
            var staticMesh = slot.AttachComponent<StaticMesh>();
            var urlField = staticMesh.GetSyncMember("URL") as Sync<Uri>;
            if (urlField != null)
                urlField.Value = new Uri(meshUrl);
            meshComponent = staticMesh;
            result["meshType"] = "StaticMesh";
            result["meshUrl"] = meshUrl;
        }
        else
        {
            // Procedural mesh
            Type procMeshType = ResolveComponentType(meshType ?? "BoxMesh");
            if (procMeshType == null)
                procMeshType = typeof(BoxMesh);
            meshComponent = slot.AttachComponent(procMeshType);
            result["meshType"] = procMeshType.Name;
        }
        result["meshRefId"] = meshComponent.ReferenceID.ToString();

        // Attach MeshRenderer
        var renderer = slot.AttachComponent<MeshRenderer>();
        result["rendererRefId"] = renderer.ReferenceID.ToString();

        // Wire mesh to renderer
        var meshRef = renderer.GetSyncMember("Mesh") as ISyncRef;
        meshRef?.TrySet(meshComponent);

        // Attach material
        Type matType = ResolveComponentType(materialType);
        if (matType == null) matType = typeof(PBS_Metallic);
        var material = slot.AttachComponent(matType);
        result["materialType"] = matType.Name;
        result["materialRefId"] = material.ReferenceID.ToString();

        // Wire material to renderer's Materials list
        var materialsField = renderer.GetSyncMember("Materials");
        if (materialsField != null)
        {
            // SyncAssetList<Material> — use reflection to call Add(IAssetProvider<Material>)
            var addMethod = materialsField.GetType().GetMethod("Add",
                new[] { typeof(IAssetProvider<Material>) });
            if (addMethod != null)
                addMethod.Invoke(materialsField, new object[] { material });
        }

        // Set color if provided
        var color = p["color"] as JArray;
        if (color != null && color.Count >= 3)
        {
            string colorFieldName = matType.Name.Contains("PBS") ? "AlbedoColor" : "TintColor";
            var colorField = material.GetSyncMember(colorFieldName) as Sync<colorX>;
            if (colorField != null)
            {
                float a = color.Count >= 4 ? color[3].Value<float>() : 1.0f;
                colorField.Value = new colorX(color[0].Value<float>(), color[1].Value<float>(), color[2].Value<float>(), a);
            }
        }

        return Ok(id, result);
    }

    private JObject HandleGetComponentField(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();
        string fieldName = p["field"]?.ToString();

        if (string.IsNullOrEmpty(fieldName))
            return Error(id, "getComponentField requires 'field'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        Type componentType = ResolveComponentType(componentName);
        if (componentType == null)
            return Error(id, $"Component type '{componentName}' not found");

        var component = slot.GetComponent(componentType);
        if (component == null)
            return Error(id, $"Component '{componentName}' not found on slot '{slotName}'");

        var member = component.GetSyncMember(fieldName);
        if (member == null)
            return Error(id, $"Field '{fieldName}' not found on {componentType.Name}");

        try
        {
            var value = ReadFieldValue(member);
            return Ok(id, new JObject
            {
                ["slot"] = slotName,
                ["component"] = componentName,
                ["field"] = fieldName,
                ["value"] = value,
                ["fieldType"] = member.GetType().Name
            });
        }
        catch (Exception ex)
        {
            return Error(id, $"Failed to read {componentName}.{fieldName}: {ex.Message}");
        }
    }

    private JObject HandleGetComponentFields(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        string componentName = p["component"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        Type componentType = ResolveComponentType(componentName);
        if (componentType == null)
            return Error(id, $"Component type '{componentName}' not found");

        var component = slot.GetComponent(componentType);
        if (component == null)
            return Error(id, $"Component '{componentName}' not found on slot '{slotName}'");

        var fields = new JArray();
        for (int i = 0; i < component.SyncMemberCount; i++)
        {
            var member = component.GetSyncMember(i);
            if (member == null) continue;

            string name = component.GetSyncMemberName(i);
            var fieldInfo = new JObject
            {
                ["name"] = name,
                ["type"] = member.GetType().Name,
            };

            try
            {
                fieldInfo["value"] = ReadFieldValue(member);
            }
            catch
            {
                fieldInfo["value"] = "<error reading>";
            }

            fields.Add(fieldInfo);
        }

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["component"] = componentName,
            ["fieldCount"] = fields.Count,
            ["fields"] = fields
        });
    }

    private JObject HandleGetSlotTransform(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["local"] = new JObject
            {
                ["position"] = new JArray(slot.LocalPosition.x, slot.LocalPosition.y, slot.LocalPosition.z),
                ["rotation"] = new JArray(slot.LocalRotation.x, slot.LocalRotation.y, slot.LocalRotation.z, slot.LocalRotation.w),
                ["scale"] = new JArray(slot.LocalScale.x, slot.LocalScale.y, slot.LocalScale.z)
            },
            ["global"] = new JObject
            {
                ["position"] = new JArray(slot.GlobalPosition.x, slot.GlobalPosition.y, slot.GlobalPosition.z),
                ["rotation"] = new JArray(slot.GlobalRotation.x, slot.GlobalRotation.y, slot.GlobalRotation.z, slot.GlobalRotation.w),
                ["scale"] = new JArray(slot.GlobalScale.x, slot.GlobalScale.y, slot.GlobalScale.z)
            }
        });
    }

    private JObject HandleListChildren(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString();
        int maxDepth = p["depth"]?.Value<int>() ?? 1;
        bool trackAll = p["trackAll"]?.Value<bool>() ?? false;

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        var children = new JArray();
        CollectChildren(slot, children, 0, maxDepth, trackAll);

        return Ok(id, new JObject
        {
            ["slot"] = slotName,
            ["depth"] = maxDepth,
            ["totalFound"] = children.Count,
            ["children"] = children
        });
    }

    private void CollectChildren(Slot parent, JArray results, int currentDepth, int maxDepth, bool trackAll)
    {
        foreach (var child in parent.Children)
        {
            var components = new JArray();
            foreach (var comp in child.Components)
                components.Add(comp.GetType().Name);

            var entry = new JObject
            {
                ["name"] = child.Name,
                ["refId"] = child.ReferenceID.ToString(),
                ["tag"] = child.Tag,
                ["active"] = child.ActiveSelf,
                ["depth"] = currentDepth + 1,
                ["childCount"] = child.ChildrenCount,
                ["components"] = components
            };

            if (trackAll)
                _tracker.Register(child.Name, child);

            results.Add(entry);

            if (maxDepth == -1 || currentDepth + 1 < maxDepth)
                CollectChildren(child, results, currentDepth + 1, maxDepth, trackAll);
        }
    }

    private JObject HandleGetSlotsByTag(string id, JObject p)
    {
        string slotName = p["slot"]?.ToString() ?? "__root__";
        string tag = p["tag"]?.ToString();
        bool trackAll = p["trackAll"]?.Value<bool>() ?? false;

        if (string.IsNullOrEmpty(tag))
            return Error(id, "getSlotsByTag requires 'tag'");

        var slot = _tracker.Get(slotName);
        if (slot == null)
            return Error(id, $"Slot '{slotName}' not found");

        var matches = slot.GetChildrenWithTag(tag);
        var results = new JArray();

        foreach (var match in matches)
        {
            if (trackAll)
                _tracker.Register(match.Name, match);

            results.Add(new JObject
            {
                ["name"] = match.Name,
                ["refId"] = match.ReferenceID.ToString(),
                ["active"] = match.ActiveSelf,
                ["childCount"] = match.ChildrenCount,
                ["componentCount"] = match.ComponentCount
            });
        }

        return Ok(id, new JObject
        {
            ["tag"] = tag,
            ["count"] = results.Count,
            ["slots"] = results
        });
    }

    private JToken ReadFieldValue(ISyncMember member)
    {
        switch (member)
        {
            case Sync<string> sf:
                return sf.Value != null ? (JToken)sf.Value : JValue.CreateNull();
            case Sync<bool> bf:
                return bf.Value;
            case Sync<int> nf:
                return nf.Value;
            case Sync<long> lf:
                return lf.Value;
            case Sync<double> df:
                return df.Value;
            case Sync<float> ff:
                return ff.Value;
            case Sync<float2> f2f:
                return new JArray(f2f.Value.x, f2f.Value.y);
            case Sync<float3> f3f:
                return new JArray(f3f.Value.x, f3f.Value.y, f3f.Value.z);
            case Sync<float4> f4f:
                return new JArray(f4f.Value.x, f4f.Value.y, f4f.Value.z, f4f.Value.w);
            case Sync<floatQ> qf:
                return new JArray(qf.Value.x, qf.Value.y, qf.Value.z, qf.Value.w);
            case Sync<colorX> cf:
                return new JArray(cf.Value.r, cf.Value.g, cf.Value.b, cf.Value.a);
            case Sync<Uri> uf:
                return uf.Value?.ToString();
            default:
                // Try SyncRef (reference fields) via ISyncRef interface
                if (member is ISyncRef syncRef)
                {
                    var target = syncRef.Target;
                    if (target != null)
                    {
                        return new JObject
                        {
                            ["refId"] = target.ReferenceID.ToString(),
                            ["type"] = target.GetType().Name,
                            ["name"] = (target is Slot s) ? s.Name : null
                        };
                    }
                    return JValue.CreateNull();
                }

                // Try enum fields via reflection
                var memberType = member.GetType();
                if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(Sync<>))
                {
                    var valueType = memberType.GetGenericArguments()[0];
                    if (valueType.IsEnum)
                    {
                        var valueProp = memberType.GetProperty("Value");
                        var enumValue = valueProp.GetValue(member);
                        return enumValue?.ToString();
                    }
                }
                return $"<unsupported:{member.GetType().Name}>";
        }
    }

    // ─── Utilities ──────────────────────────────────────────────

    private Type ResolveComponentType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        // Check built-in map first
        if (ComponentTypes.TryGetValue(typeName, out var type))
            return type;

        // Try full qualified name via reflection
        // Search in FrooxEngine assembly
        type = typeof(FrooxEngine.Slot).Assembly.GetType($"FrooxEngine.{typeName}", false, true);
        if (type != null) return type;

        // Search in UIX namespace
        type = typeof(FrooxEngine.Slot).Assembly.GetType($"FrooxEngine.UIX.{typeName}", false, true);
        if (type != null) return type;

        return null;
    }

    private void SetFieldValue(Component component, string fieldName, JToken value)
    {
        // Use reflection-based field access via ISyncMember
        var member = component.GetSyncMember(fieldName);
        if (member == null)
            throw new Exception($"Field '{fieldName}' not found on {component.GetType().Name}");

        // Handle common field types
        switch (member)
        {
            case Sync<string> sf:
                sf.Value = value.ToString();
                break;
            case Sync<bool> bf:
                bf.Value = value.Value<bool>();
                break;
            case Sync<int> nf:
                nf.Value = value.Value<int>();
                break;
            case Sync<long> lf:
                lf.Value = value.Value<long>();
                break;
            case Sync<double> df:
                df.Value = value.Value<double>();
                break;
            case Sync<float> ff:
                ff.Value = value.Value<float>();
                break;
            case Sync<float2> f2f:
                var arr2 = value as JArray;
                if (arr2 != null && arr2.Count == 2)
                    f2f.Value = new float2(arr2[0].Value<float>(), arr2[1].Value<float>());
                break;
            case Sync<float3> f3f:
                var arr3 = value as JArray;
                if (arr3 != null && arr3.Count == 3)
                    f3f.Value = new float3(arr3[0].Value<float>(), arr3[1].Value<float>(), arr3[2].Value<float>());
                break;
            case Sync<float4> f4f:
                var arr4 = value as JArray;
                if (arr4 != null && arr4.Count == 4)
                    f4f.Value = new float4(arr4[0].Value<float>(), arr4[1].Value<float>(), arr4[2].Value<float>(), arr4[3].Value<float>());
                break;
            case Sync<floatQ> qf:
                var arrQ = value as JArray;
                if (arrQ != null && arrQ.Count == 4)
                    qf.Value = new floatQ(arrQ[0].Value<float>(), arrQ[1].Value<float>(), arrQ[2].Value<float>(), arrQ[3].Value<float>());
                else if (arrQ != null && arrQ.Count == 3)
                    qf.Value = floatQ.Euler(arrQ[0].Value<float>(), arrQ[1].Value<float>(), arrQ[2].Value<float>());
                break;
            case Sync<Uri> uf:
                uf.Value = new Uri(value.ToString());
                break;
            case Sync<colorX> cf:
                var arrC = value as JArray;
                if (arrC != null && arrC.Count >= 3)
                {
                    float r = arrC[0].Value<float>();
                    float g = arrC[1].Value<float>();
                    float b = arrC[2].Value<float>();
                    float a = arrC.Count > 3 ? arrC[3].Value<float>() : 1f;
                    cf.Value = new colorX(r, g, b, a);
                }
                else if (value.Type == JTokenType.String)
                {
                    // Parse hex color like "#1A1A21"
                    string hex = value.ToString();
                    if (colorX.TryParse(hex, out var parsedColor))
                        cf.Value = parsedColor;
                }
                break;
            default:
                // Try SyncRef (reference fields) via ISyncRef interface
                if (member is ISyncRef syncRefW)
                {
                    string refValue = value.ToString();

                    // Try resolving as a tracked slot name first
                    var trackedSlot = _tracker.Get(refValue);
                    if (trackedSlot != null)
                    {
                        if (!syncRefW.TrySet(trackedSlot))
                            throw new Exception($"Type mismatch: cannot set {syncRefW.TargetType.Name} reference to Slot");
                        break;
                    }

                    // Try clearing the reference
                    if (string.IsNullOrEmpty(refValue) || refValue == "null")
                    {
                        syncRefW.Clear();
                        break;
                    }

                    throw new Exception($"Could not resolve reference '{refValue}' for field '{fieldName}'. Use a tracked slot name or 'null' to clear.");
                }

                // Try to handle enum fields via reflection
                var memberType = member.GetType();
                if (memberType.IsGenericType && memberType.GetGenericTypeDefinition() == typeof(Sync<>))
                {
                    var valueType = memberType.GetGenericArguments()[0];
                    if (valueType.IsEnum)
                    {
                        var enumValue = Enum.Parse(valueType, value.ToString(), ignoreCase: true);
                        var valueProp = memberType.GetProperty("Value");
                        valueProp.SetValue(member, enumValue);
                        break;
                    }
                }
                throw new Exception($"Unsupported field type: {member.GetType().Name} for field '{fieldName}'");
        }
    }

    // ─── Response helpers ───────────────────────────────────────

    private JObject Ok(string id, JObject result)
    {
        result["id"] = id;
        result["status"] = "ok";
        return result;
    }

    private JObject Error(string id, string message)
    {
        return new JObject
        {
            ["id"] = id,
            ["status"] = "error",
            ["error"] = message
        };
    }
}
