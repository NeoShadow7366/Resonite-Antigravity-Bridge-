using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FrooxEngine;
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
    private EventSystem _events;
    private TemplateSystem _templates;

    // ─── Handler Instances ──────────────────────────────────────
    private readonly SlotHandlers _slotHandlers;
    private readonly ComponentHandlers _componentHandlers;
    private readonly HierarchyHandlers _hierarchyHandlers;
    private readonly AssetHandlers _assetHandlers;
    private readonly UIXHandlers _uixHandlers;
    private readonly ProtoFluxHandlers _protoFluxHandlers;
    private readonly BuilderHandlers _builderHandlers;
    private readonly PhysicsHandlers _physicsHandlers;
    private readonly AnimationHandlers _animationHandlers;
    private readonly WorldHandlers _worldHandlers;
    private readonly EnvironmentHandlers _environmentHandlers;
    private readonly ReferenceHandlers _referenceHandlers;
    private readonly IntrospectionHandlers _introspectionHandlers;
    private UtilityHandlers _utilityHandlers;

    // Metrics
    private long _totalCommands;
    private long _totalErrors;
    private readonly DateTime _startTime = DateTime.UtcNow;

    public CommandRouter(SlotTracker tracker)
    {
        _tracker = tracker;

        // Instantiate all handler groups
        _slotHandlers = new SlotHandlers(tracker);
        _componentHandlers = new ComponentHandlers(tracker);
        _hierarchyHandlers = new HierarchyHandlers(tracker);
        _assetHandlers = new AssetHandlers(tracker);
        _uixHandlers = new UIXHandlers(tracker);
        _protoFluxHandlers = new ProtoFluxHandlers(tracker);
        _builderHandlers = new BuilderHandlers(tracker);
        _physicsHandlers = new PhysicsHandlers(tracker);
        _animationHandlers = new AnimationHandlers(tracker);
        _worldHandlers = new WorldHandlers(tracker);
        _environmentHandlers = new EnvironmentHandlers(tracker);
        _referenceHandlers = new ReferenceHandlers(tracker);
        _introspectionHandlers = new IntrospectionHandlers(tracker);
        // UtilityHandlers needs events/templates — created in SetEventSystem/SetTemplateSystem
    }

    /// <summary>Set the event system for subscribe/unsubscribe commands.</summary>
    public void SetEventSystem(EventSystem events)
    {
        _events = events;
        RebuildUtilityHandlers();
    }

    /// <summary>Set the template system for snapshot/template commands.</summary>
    public void SetTemplateSystem(TemplateSystem templates)
    {
        _templates = templates;
        RebuildUtilityHandlers();
    }

    private void RebuildUtilityHandlers()
    {
        _utilityHandlers = new UtilityHandlers(_tracker, _events, _templates);
    }

    public int TrackedSlotCount => _tracker.Count;

    public JObject GetTrackedSlots() => _tracker.GetAllAsJson();

    /// <summary>Returns mod status and metrics for the /status endpoint.</summary>
    public JObject GetStatus()
    {
        var uptime = DateTime.UtcNow - _startTime;
        return new JObject
        {
            ["status"] = "ok",
            ["mod"] = "AntigravityBridge",
            ["version"] = AntigravityBridge.Instance.Version,
            ["uptime"] = $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s",
            ["uptimeSeconds"] = (int)uptime.TotalSeconds,
            ["totalCommandsProcessed"] = _totalCommands,
            ["totalErrors"] = _totalErrors,
            ["trackedSlots"] = _tracker.Count,
            ["eventSubscriptions"] = _events?.SubscriptionCount ?? 0,
            ["savedTemplates"] = _templates?.TemplateCount ?? 0,
            ["commandCount"] = RequiredParams.Count,
            ["apiVersion"] = 1
        };
    }

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
                ["makePhysicsObject"] = new JObject { ["params"] = "slot, collider? (box|sphere|capsule|mesh), grabbable? (true), mass?", ["description"] = "Add collider + CharacterController + Grabbable in one call" },
                ["importAudio"] = new JObject { ["params"] = "url, parent?, trackAs?, spatial? (true)", ["description"] = "Import audio from URL: creates StaticAudioClip + AudioClipPlayer + AudioOutput" },
                ["setupEnvironment"] = new JObject { ["params"] = "parent?, trackAs?, skybox? (true), ambient? (true), reflectionProbe? (true)", ["description"] = "Create skybox + ambient light + reflection probe in one call" },
                ["createParticleSystem"] = new JObject { ["params"] = "parent?, trackAs?, emitter? (point|cone|box|sphere), rate?, lifetime?, size?, color?", ["description"] = "Create a complete particle system with emitter, style, and renderer" },
                ["createAnimation"] = new JObject { ["params"] = "slot, targetComponent, targetField, type?, keyframes[], duration?, loop?, componentIndex?", ["description"] = "Create a ValueGradientDriver animation with keyframes driving a target field" },
                ["removeFromList"] = new JObject { ["params"] = "slot, component, field, index? (default: last), componentIndex?", ["description"] = "Remove an item from a SyncList field by index" },
                ["copyComponent"] = new JObject { ["params"] = "sourceSlot, targetSlot, component, componentIndex?", ["description"] = "Duplicate a component from one slot to another" },
                ["setSlotPersist"] = new JObject { ["params"] = "slot, persistent? (true)", ["description"] = "Set whether a slot persists across sessions" },
                ["importVideo"] = new JObject { ["params"] = "url, parent?, trackAs?, autoPlay?", ["description"] = "Import video from URL with display quad and material" },
                ["createLight"] = new JObject { ["params"] = "parent?, trackAs?, lightType? (point|directional|spot), intensity?, range?, shadows?, color?, position?", ["description"] = "Create a light with type, color, and shadow settings" },
                ["createMaterial"] = new JObject { ["params"] = "slot, materialType? (PBS_Metallic), color?, metallic?, smoothness?, rendererSlot?, trackAs?", ["description"] = "Create a material, configure PBR properties, and optionally wire to a renderer" },
                ["create3DText"] = new JObject { ["params"] = "parent?, text?, trackAs?, fontSize?, color?, position?, horizontalAlign?", ["description"] = "Create 3D text with TextRenderer + UnlitMaterial" },
                ["measureDistance"] = new JObject { ["params"] = "slotA, slotB", ["description"] = "Measure distance between two slots with position and delta" },
                ["setFieldOnChildren"] = new JObject { ["params"] = "slot, component, field, value, maxDepth?", ["description"] = "Set a field on all matching components in children (recursive)" },
                ["moveUser"] = new JObject { ["params"] = "position?, rotation?, targetSlot?", ["description"] = "Teleport the local user to a position or to a slot" },
                ["duplicateSlotArray"] = new JObject { ["params"] = "slot, count?, spacing?, trackPrefix?", ["description"] = "Create N copies of a slot with uniform spacing" },
                ["clearTracker"] = new JObject { ["params"] = "none", ["description"] = "Clear all name→slot mappings" },
                ["trackExistingSlot"] = new JObject { ["params"] = "path, from?, trackAs?", ["description"] = "Find an existing slot by hierarchy path and register it in tracker" },
                ["buildUIXTree"] = new JObject { ["params"] = "parent?, root{}", ["description"] = "Build entire UI hierarchy from a declarative JSON tree" },
                ["getWorldInfo"] = new JObject { ["params"] = "none", ["description"] = "Get current world name, session ID, user count, and host" },
                ["getUserInfo"] = new JObject { ["params"] = "none", ["description"] = "Get local user's name, position, rotation, and root slot" },
                ["getUsers"] = new JObject { ["params"] = "none", ["description"] = "List all users in the current world with names and positions" },
                ["findComponents"] = new JObject { ["params"] = "type, slot?, maxDepth?, trackMatches?", ["description"] = "Search hierarchy for slots containing a specific component type" },
                ["getRegisteredComponents"] = new JObject { ["params"] = "none", ["description"] = "List all registered component shortcut names and their full types" },
                ["createProtoFluxNode"] = new JObject { ["params"] = "slot, nodeType, trackAs?", ["description"] = "Create a ProtoFlux node on a slot by binding type name" },
                ["connectProtoFlux"] = new JObject { ["params"] = "sourceSlot, sourceOutput, targetSlot, targetInput, sourceComponent?, targetComponent?", ["description"] = "Wire a ProtoFlux node output to another node's input" },
                ["setProtoFluxInput"] = new JObject { ["params"] = "slot, field, value, component?", ["description"] = "Set a constant value on a ProtoFlux node's Sync field" },
                ["getProtoFluxNode"] = new JObject { ["params"] = "slot, component?", ["description"] = "Inspect a ProtoFlux node's inputs, outputs, and current state" },
                ["subscribe"] = new JObject { ["params"] = "eventType, slot?, component?, field?", ["description"] = "Subscribe to events (fieldChanged, slotChildrenChanged, slotDestroyed, userJoin, userLeave). Requires WebSocket connection." },
                ["unsubscribe"] = new JObject { ["params"] = "subscriptionId | all", ["description"] = "Unsubscribe from events by ID, or all" },
                ["listSubscriptions"] = new JObject { ["params"] = "none", ["description"] = "List all active event subscriptions" },
                ["snapshotSlot"] = new JObject { ["params"] = "slot, maxDepth?, includeComponents?", ["description"] = "Serialize a slot hierarchy to a JSON snapshot" },
                ["saveTemplate"] = new JObject { ["params"] = "slot, templateName, maxDepth?", ["description"] = "Save a slot hierarchy as a named reusable template" },
                ["stampTemplate"] = new JObject { ["params"] = "templateName, slot, trackAs?", ["description"] = "Create a copy of a saved template under a parent slot" },
                ["listTemplates"] = new JObject { ["params"] = "none", ["description"] = "List all saved templates" },
                ["deleteTemplate"] = new JObject { ["params"] = "templateName", ["description"] = "Delete a saved template" },
                ["findSlotByPath"] = new JObject { ["params"] = "path, from?, trackAs?", ["description"] = "Navigate to a slot by slash-delimited path (e.g. 'Root/Panel/Header')" },
                ["findSlots"] = new JObject { ["params"] = "name?, tag?, regex?, searchRoot?, maxDepth?, maxResults?, trackAll?", ["description"] = "Multi-result search with optional regex" },
                ["getParent"] = new JObject { ["params"] = "slot, trackAs?", ["description"] = "Get parent slot info and optionally track it" },
                ["getSlotHierarchy"] = new JObject { ["params"] = "slot, maxDepth?", ["description"] = "Get a tree view of the slot hierarchy" },
                ["wireReference"] = new JObject { ["params"] = "slot, component, field, targetRefId, componentIndex?", ["description"] = "Wire any ISyncRef field to any world element by RefID" },
                ["addToList"] = new JObject { ["params"] = "slot, component, field, targetRefId?, value?, componentIndex?", ["description"] = "Add an item to a SyncList field" },
                ["getComponentByRefId"] = new JObject { ["params"] = "refId", ["description"] = "Look up any component by RefID, return type + slot + fields" },
                ["getAllComponents"] = new JObject { ["params"] = "slot", ["description"] = "List ALL components on a slot with types, RefIDs, and field names" },
                // New: Global transform & orientation
                ["setGlobalTransform"] = new JObject { ["params"] = "slot, position?, rotation?, scale?", ["description"] = "Set world-space (global) position, rotation, and/or scale" },
                ["lookAt"] = new JObject { ["params"] = "slot, target? (slot name), position? ([x,y,z]), up?", ["description"] = "Orient a slot to face a target slot or world position" },
                // Introspection / Discovery
                ["describeComponentType"] = new JObject { ["params"] = "type", ["description"] = "Describe all fields on a component TYPE with types, enums, and reference targets (no instance needed)" },
                ["searchComponents"] = new JObject { ["params"] = "query, maxResults?, registeredOnly?", ["description"] = "Fuzzy search component type names across registry and FrooxEngine assembly" },
                ["getFieldType"] = new JObject { ["params"] = "slot, component, field, componentIndex?", ["description"] = "Get exact type info for a field including enum values and current value" },
            },
            ["endpoints"] = new JObject
            {
                ["/ping"] = "GET — health check",
                ["/cmd"] = "POST — execute single command",
                ["/batch"] = "POST — execute batch (single engine dispatch)",
                ["/tracker"] = "GET — list tracked slots",
                ["/help"] = "GET — this help",
                ["/status"] = "GET — server status, uptime, command counts",
                ["/ws"] = "WebSocket — bidirectional streaming for commands + events"
            },
            ["fieldTypes"] = new JArray("string", "bool", "byte", "short", "ushort", "int", "uint", "long", "float", "double", "float2", "float3", "float4", "floatQ", "int2", "int3", "Rect", "colorX", "Uri", "enum (auto)", "SyncRef (auto + RefID)"),
            ["registeredComponents"] = new JArray(ComponentRegistry.ComponentTypes.Keys.OrderBy(k => k).ToArray()),
            ["specialSlots"] = new JArray("__root__", "__worldroot__", "__localuser__"),
            ["apiVersion"] = 1
        };
    }

    // ─── Dispatch ───────────────────────────────────────────────

    /// <summary>
    /// Execute the action logic for a command. Must be called on the engine thread.
    /// Delegates to the appropriate handler instance.
    /// </summary>
    private JObject ExecuteAction(string id, string action, JObject p)
    {
        return action switch
        {
            // Ping / Log / Tracker
            "ping" => Ok(id, new JObject { ["message"] = "pong" }),
            "log" => _utilityHandlers.HandleLog(id, p),
            "cleartracker" => _utilityHandlers.HandleClearTracker(id),

            // Slot operations
            "createslot" => _slotHandlers.HandleCreateSlot(id, p),
            "setslotactive" => _slotHandlers.HandleSetSlotActive(id, p),
            "destroyslot" => _slotHandlers.HandleDestroySlot(id, p),
            "destroychildren" => _slotHandlers.HandleDestroyChildren(id, p),
            "reparentslot" => _slotHandlers.HandleReparentSlot(id, p),
            "setslotname" => _slotHandlers.HandleSetSlotName(id, p),
            "setslottag" => _slotHandlers.HandleSetSlotTag(id, p),
            "setslotorderindex" => _slotHandlers.HandleSetSlotOrderIndex(id, p),
            "duplicateslot" => _slotHandlers.HandleDuplicateSlot(id, p),
            "setslotpersist" => _slotHandlers.HandleSetSlotPersist(id, p),
            "setslottransform" => _slotHandlers.HandleSetSlotTransform(id, p),
            "setglobaltransform" => _slotHandlers.HandleSetGlobalTransform(id, p),
            "lookat" => _slotHandlers.HandleLookAt(id, p),

            // Component operations
            "attachcomponent" => _componentHandlers.HandleAttachComponent(id, p),
            "createcomponent" => _componentHandlers.HandleAttachComponent(id, p), // alias
            "removecomponent" => _componentHandlers.HandleRemoveComponent(id, p),
            "copycomponent" => _componentHandlers.HandleCopyComponent(id, p),
            "setfield" => _componentHandlers.HandleSetField(id, p),
            "setfields" => _componentHandlers.HandleSetFields(id, p),
            "getcomponentfield" => _componentHandlers.HandleGetComponentField(id, p),
            "getcomponentfields" => _componentHandlers.HandleGetComponentFields(id, p),
            "findcomponents" => _componentHandlers.HandleFindComponents(id, p),
            "getregisteredcomponents" => _componentHandlers.HandleGetRegisteredComponents(id),
            "getcomponentbyrefid" => _componentHandlers.HandleGetComponentByRefId(id, p),
            "getallcomponents" => _componentHandlers.HandleGetAllComponents(id, p),

            // Hierarchy / Navigation
            "getslotinfo" => _hierarchyHandlers.HandleGetSlotInfo(id, p),
            "getslottransform" => _hierarchyHandlers.HandleGetSlotTransform(id, p),
            "listchildren" => _hierarchyHandlers.HandleListChildren(id, p),
            "getslotsbytag" => _hierarchyHandlers.HandleGetSlotsByTag(id, p),
            "findslot" => _hierarchyHandlers.HandleFindSlot(id, p),
            "findslots" => _hierarchyHandlers.HandleFindSlots(id, p),
            "findslotbypath" => _hierarchyHandlers.HandleFindSlotByPath(id, p),
            "trackexistingslot" => _hierarchyHandlers.HandleTrackExistingSlot(id, p),
            "getparent" => _hierarchyHandlers.HandleGetParent(id, p),
            "getslothierarchy" => _hierarchyHandlers.HandleGetSlotHierarchy(id, p),

            // Asset import
            "importtexture" => _assetHandlers.HandleImportTexture(id, p),
            "importmesh" => _assetHandlers.HandleImportMesh(id, p),
            "importaudio" => _assetHandlers.HandleImportAudio(id, p),
            "importvideo" => _assetHandlers.HandleImportVideo(id, p),

            // UIX
            "builduixtree" => _uixHandlers.HandleBuildUIXTree(id, p),

            // ProtoFlux
            "createprotofluxnode" => _protoFluxHandlers.HandleCreateProtoFluxNode(id, p),
            "connectprotoflux" => _protoFluxHandlers.HandleConnectProtoFlux(id, p),
            "setprotofluxinput" => _protoFluxHandlers.HandleSetProtoFluxInput(id, p),
            "getprotofluxnode" => _protoFluxHandlers.HandleGetProtoFluxNode(id, p),

            // Builder (primitives, materials, text, lights)
            "createprimitive" => _builderHandlers.HandleCreatePrimitive(id, p),
            "creatematerial" => _builderHandlers.HandleCreateMaterial(id, p),
            "create3dtext" => _builderHandlers.HandleCreate3DText(id, p),
            "createlight" => _builderHandlers.HandleCreateLight(id, p),

            // Physics
            "makephysicsobject" => _physicsHandlers.HandleMakePhysicsObject(id, p),
            "createparticlesystem" => _physicsHandlers.HandleCreateParticleSystem(id, p),

            // Animation
            "createanimation" => _animationHandlers.HandleCreateAnimation(id, p),

            // World
            "getworldinfo" => _worldHandlers.HandleGetWorldInfo(id),
            "getuserinfo" => _worldHandlers.HandleGetUserInfo(id),
            "getusers" => _worldHandlers.HandleGetUsers(id),
            "moveuser" => _worldHandlers.HandleMoveUser(id, p),

            // Environment
            "setupenvironment" => _environmentHandlers.HandleSetupEnvironment(id, p),

            // Reference wiring & lists
            "wirereference" => _referenceHandlers.HandleWireReference(id, p),
            "addtolist" => _referenceHandlers.HandleAddToList(id, p),
            "removefromlist" => _referenceHandlers.HandleRemoveFromList(id, p),

            // Dynamic variables
            "createdynvarspace" => _utilityHandlers.HandleCreateDynVarSpace(id, p),
            "createdynvar" => _utilityHandlers.HandleCreateDynVar(id, p),
            "readdynvar" => _utilityHandlers.HandleReadDynVar(id, p),
            "writedynvar" => _utilityHandlers.HandleWriteDynVar(id, p),

            // Events
            "subscribe" => _utilityHandlers.HandleSubscribe(id, p),
            "unsubscribe" => _utilityHandlers.HandleUnsubscribe(id, p),
            "listsubscriptions" => _utilityHandlers.HandleListSubscriptions(id),

            // Templates
            "snapshotslot" => _utilityHandlers.HandleSnapshotSlot(id, p),
            "savetemplate" => _utilityHandlers.HandleSaveTemplate(id, p),
            "stamptemplate" => _utilityHandlers.HandleStampTemplate(id, p),
            "listtemplates" => _utilityHandlers.HandleListTemplates(id),
            "deletetemplate" => _utilityHandlers.HandleDeleteTemplate(id, p),

            // Batch utilities
            "measuredistance" => _utilityHandlers.HandleMeasureDistance(id, p),
            "setfieldonchildren" => _utilityHandlers.HandleSetFieldOnChildren(id, p),
            "duplicateslotarray" => _utilityHandlers.HandleDuplicateSlotArray(id, p),

            // Introspection / Discovery
            "describecomponenttype" => _introspectionHandlers.HandleDescribeComponentType(id, p),
            "searchcomponents" => _introspectionHandlers.HandleSearchComponents(id, p),
            "getfieldtype" => _introspectionHandlers.HandleGetFieldType(id, p),

            // Aliases
            "deleteslot" => _slotHandlers.HandleDestroySlot(id, p),

            _ => Error(id, $"Unknown action: {action}")
        };
    }

    // ─── Validation ─────────────────────────────────────────────

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
        ["getWorldInfo"] = Array.Empty<string>(),
        ["getUserInfo"] = Array.Empty<string>(),
        ["getUsers"] = Array.Empty<string>(),
        ["findComponents"] = new[] { "type" },
        ["getRegisteredComponents"] = Array.Empty<string>(),
        ["createProtoFluxNode"] = new[] { "slot", "nodeType" },
        ["connectProtoFlux"] = new[] { "sourceSlot", "sourceOutput", "targetSlot", "targetInput" },
        ["setProtoFluxInput"] = new[] { "slot", "field", "value" },
        ["getProtoFluxNode"] = new[] { "slot" },
        ["subscribe"] = new[] { "eventType" },
        ["unsubscribe"] = Array.Empty<string>(),
        ["listSubscriptions"] = Array.Empty<string>(),
        ["snapshotSlot"] = new[] { "slot" },
        ["saveTemplate"] = new[] { "slot", "templateName" },
        ["stampTemplate"] = new[] { "templateName", "slot" },
        ["listTemplates"] = Array.Empty<string>(),
        ["deleteTemplate"] = new[] { "templateName" },
        ["findSlotByPath"] = new[] { "path" },
        ["findSlots"] = Array.Empty<string>(),
        ["getParent"] = new[] { "slot" },
        ["getSlotHierarchy"] = new[] { "slot" },
        ["wireReference"] = new[] { "slot", "component", "field", "targetRefId" },
        ["addToList"] = new[] { "slot", "component", "field" },
        ["getComponentByRefId"] = new[] { "refId" },
        ["getAllComponents"] = new[] { "slot" },
        ["ping"] = Array.Empty<string>(),
        ["log"] = Array.Empty<string>(),
        ["makePhysicsObject"] = new[] { "slot" },
        ["importAudio"] = new[] { "url" },
        ["setupEnvironment"] = Array.Empty<string>(),
        ["createParticleSystem"] = Array.Empty<string>(),
        ["createAnimation"] = new[] { "slot", "targetComponent", "targetField" },
        ["removeFromList"] = new[] { "slot", "component", "field" },
        ["copyComponent"] = new[] { "sourceSlot", "targetSlot", "component" },
        ["setSlotPersist"] = new[] { "slot" },
        ["importVideo"] = new[] { "url" },
        ["createLight"] = Array.Empty<string>(),
        ["createMaterial"] = new[] { "slot" },
        ["create3DText"] = Array.Empty<string>(),
        ["measureDistance"] = new[] { "slotA", "slotB" },
        ["setFieldOnChildren"] = new[] { "slot", "component", "field", "value" },
        ["moveUser"] = Array.Empty<string>(),
        ["duplicateSlotArray"] = new[] { "slot" },
        ["clearTracker"] = Array.Empty<string>(),
        // New commands
        ["setGlobalTransform"] = new[] { "slot" },
        ["lookAt"] = new[] { "slot" },
        ["describeComponentType"] = new[] { "type" },
        ["searchComponents"] = new[] { "query" },
        ["getFieldType"] = new[] { "slot", "component", "field" },
        // Aliases
        ["createComponent"] = new[] { "slot", "type" },
        ["deleteSlot"] = new[] { "slot" },
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

    // ─── Execution ──────────────────────────────────────────────

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
        {
            Interlocked.Increment(ref _totalErrors);
            return validationError;
        }

        Interlocked.Increment(ref _totalCommands);

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

            var timeout = AntigravityBridge.CommandTimeoutSeconds;
            if (!tcs.Task.Wait(TimeSpan.FromSeconds(timeout)))
                return Error(id, $"Command timed out ({timeout}s) waiting for engine thread");
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

                        if (cmd["__validated_error"]?.Value<bool>() == true)
                            continue;

                        string id = cmd["id"]?.ToString();
                        if (string.IsNullOrEmpty(id))
                            id = $"batch_{i}";

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

                        result["commandIndex"] = i;
                        results.Add(result);

                        Interlocked.Increment(ref _totalCommands);

                        if (result["status"]?.ToString() == "ok")
                            successCount++;
                        else
                        {
                            Interlocked.Increment(ref _totalErrors);
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
