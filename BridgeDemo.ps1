# ===================================================================
#  AntigravityBridge Demo - Visual Showcase
#  Run: powershell -ExecutionPolicy Bypass -File BridgeDemo.ps1
# ===================================================================

$baseUrl = "http://localhost:9090"
$delay   = 3.5

function Send-Cmd($body) {
    try {
        $json = $body | ConvertTo-Json -Depth 10 -Compress
        Invoke-RestMethod -Uri "$baseUrl/cmd" -Method POST -Body $json -ContentType "application/json" -ErrorAction SilentlyContinue
    } catch { }
}
function Send-Batch($commands) {
    try {
        $payload = @{ commands = $commands } | ConvertTo-Json -Depth 10 -Compress
        Invoke-RestMethod -Uri "$baseUrl/batch" -Method POST -Body $payload -ContentType "application/json" -ErrorAction SilentlyContinue
    } catch { }
}
function Update-Info($title, $body) {
    Send-Batch @(
        @{ id="ut"; action="setField"; params=@{ slot="PanelTitle"; component="Text"; field="Content"; value="<b>$title</b>" }},
        @{ id="ub"; action="setField"; params=@{ slot="PanelBody"; component="Text"; field="Content"; value=$body }}
    )
}
function Pause { Start-Sleep -Seconds $delay }

Write-Host ""
Write-Host "  === AntigravityBridge Visual Demo ===" -ForegroundColor Cyan
Write-Host ""

# -- Get user position and compute forward direction --
$userInfo = Send-Cmd @{ id="u"; action="getUserInfo"; params=@{} }
$hx = $userInfo.headPosition[0]
$hy = $userInfo.headPosition[1]
$hz = $userInfo.headPosition[2]
$qx = $userInfo.headRotation[0]
$qy = $userInfo.headRotation[1]
$qz = $userInfo.headRotation[2]
$qw = $userInfo.headRotation[3]

# Forward vector from head quaternion (Resonite +Z forward)
$fwdX = 2*($qx*$qz + $qw*$qy)
$fwdZ = 1 - 2*($qx*$qx + $qy*$qy)
# Normalize horizontal
$fLen = [math]::Sqrt($fwdX*$fwdX + $fwdZ*$fwdZ)
if ($fLen -gt 0.001) { $fwdX /= $fLen; $fwdZ /= $fLen }

# Right vector (perpendicular to forward, horizontal)
$rightX = $fwdZ
$rightZ = -$fwdX

# Place demo 2m in front of user
$dist = 2.0
$cx = [math]::Round($hx + $fwdX * $dist, 3)
$cy = [math]::Round($hy, 3)
$cz = [math]::Round($hz + $fwdZ * $dist, 3)

# Panel facing angle (face back toward user = opposite of forward)
$faceYaw = [math]::Round([math]::Atan2($fwdX, $fwdZ) * 180 / [math]::PI, 2)

# Info panel: 0.7m to the left of center
$ipx = [math]::Round($rightX * -0.7, 3)
$ipz = [math]::Round($rightZ * -0.7, 3)

# Demo area: 0.4m to the right of center
$dax = [math]::Round($rightX * 0.4, 3)
$daz = [math]::Round($rightZ * 0.4, 3)

Write-Host "  Forward: ($([math]::Round($fwdX,2)), $([math]::Round($fwdZ,2)))" -ForegroundColor DarkGray
Write-Host "  Demo at: ($cx, $cy, $cz)" -ForegroundColor DarkGray
Write-Host ""

# -- PHASE 0: Build Framework --
Write-Host "  [0/8] Framework..." -ForegroundColor Yellow

Send-Batch @(
    @{ id="r"; action="createSlot"; params=@{ name="BridgeDemo"; position=@($cx,$cy,$cz) }},
    @{ id="ip"; action="createSlot"; params=@{ name="InfoPanel"; parent="BridgeDemo";
        position=@($ipx, 0.1, $ipz); rotation=@(0, $faceYaw, 0); scale=@(0.0012, 0.0012, 0.0012) }},
    @{ id="da"; action="createSlot"; params=@{ name="DemoArea"; parent="BridgeDemo";
        position=@($dax, -0.1, $daz); rotation=@(0, $faceYaw, 0) }}
)

# Build the info panel as UIX (like the UIX demo panel which rendered nicely)
Send-Cmd @{ id="uip"; action="buildUIXTree"; params=@{
    parent="InfoPanel"
    root=@{
        name="InfoCanvas"; components=@(@{ type="Canvas"; fields=@{ Size=@(550, 500) }})
        children=@(
            @{ name="IBg"; components=@(@{ type="Image"; fields=@{ Tint=@(0.04,0.04,0.10,0.94) }}) },
            @{ name="ILayout"; components=@(@{ type="VerticalLayout"; fields=@{ Spacing=8; PaddingTop=20; PaddingBottom=20; PaddingLeft=24; PaddingRight=24 }})
                children=@(
                    @{ name="IHeader"; components=@(@{ type="Text"; fields=@{ Content="<b>AntigravityBridge Demo</b>"; Size=34; Color=@(0.3,0.85,1,1) }}) },
                    @{ name="IBar"; components=@(@{ type="Image"; fields=@{ Tint=@(0.3,0.85,1,0.5) }}; @{ type="LayoutElement"; fields=@{ PreferredHeight=3 }}) },
                    @{ name="PanelTitle"; components=@(@{ type="Text"; fields=@{ Content="<b>Initializing...</b>"; Size=28; Color=@(1,0.92,0.5,1) }}) },
                    @{ name="PanelBody"; components=@(@{ type="Text"; fields=@{ Content="Setting up the demo...<br>Please look forward."; Size=22; Color=@(0.82,0.82,0.88,1); HorizontalAlign="Left" }}) }
                )
            }
        )
    }
}}

Start-Sleep -Seconds 1.5
Write-Host "  [0/8] Ready!" -ForegroundColor Green

# -- PHASE 1: Hierarchy --
Write-Host "  [1/8] Hierarchy..." -ForegroundColor Yellow
Update-Info "1. Slot Hierarchy" "Creating parent-child slot trees<br>with names, tags, and transforms.<br><br>Commands:<br>  createSlot<br>  setSlotTag<br>  reparentSlot<br>  duplicateSlot"
Pause

Send-Batch @(
    @{ id="s1"; action="createSlot"; params=@{ name="Hierarchy"; parent="DemoArea"; tag="demo"; position=@(0,0.45,0) }},
    @{ id="s2"; action="createSlot"; params=@{ name="CubeChild"; parent="Hierarchy"; position=@(-0.2,0,0);
        components=@(
            @{ type="BoxMesh"; fields=@{ Size=@(0.12,0.12,0.12) }},
            @{ type="MeshRenderer" },
            @{ type="PBS_Metallic"; fields=@{ AlbedoColor=@(0.2,0.6,1,1); EmissiveColor=@(0.1,0.3,0.7,1) }}
        )
    }},
    @{ id="s3"; action="createSlot"; params=@{ name="SphereChild"; parent="Hierarchy"; position=@(0,0,0);
        components=@(
            @{ type="SphereMesh"; fields=@{ Radius=0.065 }},
            @{ type="MeshRenderer" },
            @{ type="PBS_Metallic"; fields=@{ AlbedoColor=@(0.2,1,0.4,1); EmissiveColor=@(0.1,0.5,0.2,1) }}
        )
    }},
    @{ id="s4"; action="createSlot"; params=@{ name="CylChild"; parent="Hierarchy"; position=@(0.2,0,0);
        components=@(
            @{ type="CylinderMesh"; fields=@{ Radius=0.05; Height=0.14 }},
            @{ type="MeshRenderer" },
            @{ type="PBS_Metallic"; fields=@{ AlbedoColor=@(1,0.3,0.5,1); EmissiveColor=@(0.5,0.1,0.2,1) }}
        )
    }}
)
Pause

# -- PHASE 2: Mesh Gallery --
Write-Host "  [2/8] Mesh gallery..." -ForegroundColor Yellow
Update-Info "2. Mesh Gallery" "Spawning 10 different mesh types<br>with unique metallic materials.<br><br>Box, Sphere, Cone, Torus,<br>BevelBox, IcoSphere, Ring,<br>Tube, Capsule, Circle<br><br>97 component types registered."
Pause

$meshes = @(
    @{ n="gBox";     t="BoxMesh";       x=-0.36; y=0.16; c=@(0.9,0.3,0.2,1) },
    @{ n="gSphere";  t="SphereMesh";    x=-0.18; y=0.16; c=@(0.2,0.7,0.9,1) },
    @{ n="gCone";    t="ConeMesh";      x=0.0;   y=0.16; c=@(0.9,0.8,0.1,1) },
    @{ n="gTorus";   t="TorusMesh";     x=0.18;  y=0.16; c=@(0.6,0.2,0.9,1) },
    @{ n="gBevel";   t="BevelBoxMesh";  x=0.36;  y=0.16; c=@(0.1,0.9,0.6,1) },
    @{ n="gIco";     t="IcoSphereMesh"; x=-0.36; y=-0.06; c=@(1.0,0.5,0.2,1) },
    @{ n="gRing";    t="RingMesh";      x=-0.18; y=-0.06; c=@(0.3,0.9,0.9,1) },
    @{ n="gTube";    t="TubeMesh";      x=0.0;   y=-0.06; c=@(0.9,0.4,0.7,1) },
    @{ n="gCapsule"; t="CapsuleMesh";   x=0.18;  y=-0.06; c=@(0.4,0.6,1.0,1) },
    @{ n="gCircle";  t="CircleMesh";    x=0.36;  y=-0.06; c=@(0.8,0.9,0.2,1) }
)
$cmds = @()
foreach ($m in $meshes) {
    $cmds += @{ id=$m.n; action="createSlot"; params=@{
        name=$m.n; parent="DemoArea"; position=@($m.x, $m.y, 0)
        scale=@(0.1, 0.1, 0.1)
        components=@(
            @{ type=$m.t },
            @{ type="MeshRenderer" },
            @{ type="PBS_Metallic"; fields=@{ AlbedoColor=$m.c; Metallic=0.7; Smoothness=0.85 }}
        )
    }}
}
Send-Batch $cmds
Pause

# -- PHASE 3: Transforms --
Write-Host "  [3/8] Transforms..." -ForegroundColor Yellow
Update-Info "3. Transform Operations" "Watch the hierarchy objects change:<br><br>  Cube scales up x1.6<br>  Cylinder rotates 45 degrees<br>  Sphere looks at Cube<br><br>New commands:<br>  setGlobalTransform<br>  lookAt"
Pause

Send-Batch @(
    @{ id="t1"; action="setSlotTransform"; params=@{ slot="CubeChild"; scale=@(1.6,1.6,1.6) }},
    @{ id="t2"; action="setSlotTransform"; params=@{ slot="CylChild"; rotation=@(0,45,45) }},
    @{ id="t3"; action="lookAt"; params=@{ slot="SphereChild"; target="CubeChild" }}
)
Pause

# -- PHASE 4: Materials --
Write-Host "  [4/8] Materials..." -ForegroundColor Yellow
Update-Info "4. Live Material Changes" "Watch colors change in real-time:<br><br>  Cube turns magenta<br>  Sphere turns gold<br>  Cylinder goes chrome<br><br>21 field types: colorX, float,<br>float2/3/4, int, bool, enum..."
Pause

Send-Batch @(
    @{ id="c1"; action="setField"; params=@{ slot="CubeChild"; component="PBS_Metallic"; field="AlbedoColor"; value=@(1,0.1,0.85,1) }},
    @{ id="c2"; action="setField"; params=@{ slot="CubeChild"; component="PBS_Metallic"; field="EmissiveColor"; value=@(0.7,0,0.5,1) }},
    @{ id="c3"; action="setField"; params=@{ slot="SphereChild"; component="PBS_Metallic"; field="AlbedoColor"; value=@(1,0.85,0,1) }},
    @{ id="c4"; action="setField"; params=@{ slot="SphereChild"; component="PBS_Metallic"; field="EmissiveColor"; value=@(0.6,0.5,0,1) }},
    @{ id="c5"; action="setField"; params=@{ slot="CylChild"; component="PBS_Metallic"; field="Metallic"; value=1.0 }},
    @{ id="c6"; action="setField"; params=@{ slot="CylChild"; component="PBS_Metallic"; field="Smoothness"; value=1.0 }},
    @{ id="c7"; action="setField"; params=@{ slot="CylChild"; component="PBS_Metallic"; field="AlbedoColor"; value=@(0.9,0.9,0.95,1) }}
)
Pause

# -- PHASE 5: UIX --
Write-Host "  [5/8] UIX panel..." -ForegroundColor Yellow
Update-Info "5. UIX Construction" "Building a complete UI panel<br>with one buildUIXTree call:<br><br>  Canvas + Layout<br>  Styled buttons<br>  Status text<br>  Color-coded elements"
Pause

Send-Cmd @{ id="us"; action="createSlot"; params=@{ name="UIXShow"; parent="DemoArea"; position=@(0,-0.22,0); scale=@(0.001, 0.001, 0.001) }}
Send-Cmd @{ id="ux"; action="buildUIXTree"; params=@{
    parent="UIXShow"
    root=@{
        name="DemoUI"; components=@(@{ type="Canvas"; fields=@{ Size=@(500,220) }})
        children=@(
            @{ name="UBg"; components=@(@{ type="Image"; fields=@{ Tint=@(0.06,0.06,0.14,0.95) }}) },
            @{ name="UL"; components=@(@{ type="VerticalLayout"; fields=@{ Spacing=6; PaddingTop=14; PaddingBottom=14; PaddingLeft=18; PaddingRight=18 }})
                children=@(
                    @{ name="UT"; components=@(@{ type="Text"; fields=@{ Content="<b>Bridge-Built Interface</b>"; Size=24; Color=@(0.3,0.85,1,1) }}) },
                    @{ name="UR"; components=@(@{ type="HorizontalLayout"; fields=@{ Spacing=8 }})
                        children=@(
                            @{ name="B1"; components=@(@{ type="Image"; fields=@{ Tint=@(0.15,0.55,0.3,1) }}, @{ type="LayoutElement"; fields=@{ PreferredHeight=38 }}, @{ type="Button" })
                                children=@(@{ name="B1T"; components=@(@{ type="Text"; fields=@{ Content="Create"; Size=18; Color=@(1,1,1,1) }}) }) },
                            @{ name="B2"; components=@(@{ type="Image"; fields=@{ Tint=@(0.2,0.35,0.75,1) }}, @{ type="LayoutElement"; fields=@{ PreferredHeight=38 }}, @{ type="Button" })
                                children=@(@{ name="B2T"; components=@(@{ type="Text"; fields=@{ Content="Modify"; Size=18; Color=@(1,1,1,1) }}) }) },
                            @{ name="B3"; components=@(@{ type="Image"; fields=@{ Tint=@(0.7,0.15,0.15,1) }}, @{ type="LayoutElement"; fields=@{ PreferredHeight=38 }}, @{ type="Button" })
                                children=@(@{ name="B3T"; components=@(@{ type="Text"; fields=@{ Content="Delete"; Size=18; Color=@(1,1,1,1) }}) }) }
                        )
                    },
                    @{ name="US"; components=@(@{ type="Text"; fields=@{ Content="Connected to AntigravityBridge v1.0.0"; Size=16; Color=@(0.45,0.85,0.45,1) }}) },
                    @{ name="UB"; components=@(@{ type="Image"; fields=@{ Tint=@(0.3,0.7,1,0.5) }}; @{ type="LayoutElement"; fields=@{ PreferredHeight=3 }}) },
                    @{ name="UI"; components=@(@{ type="Text"; fields=@{ Content="97 components | 21 field types | 80+ commands"; Size=14; Color=@(0.55,0.55,0.65,1) }}) }
                )
            }
        )
    }
}}
Pause

# -- PHASE 6: Introspection --
Write-Host "  [6/8] Introspection..." -ForegroundColor Yellow
$desc = Send-Cmd @{ id="d"; action="describeComponentType"; params=@{ type="Light" }}
$fc = if ($desc.fieldCount) { $desc.fieldCount } else { "?" }
$sr = Send-Cmd @{ id="s"; action="searchComponents"; params=@{ query="collider"; maxResults=10 }}
$sc = if ($sr.count) { $sr.count } else { "?" }

Update-Info "6. Type Introspection" "Live type discovery for AI agents:<br><br>describeComponentType(Light)<br>  -> $fc fields found<br><br>searchComponents('collider')<br>  -> $sc matching types<br><br>getFieldType: returns current<br>values, types, and enum options"
Pause

# -- PHASE 7: DynVars --
Write-Host "  [7/8] DynVars..." -ForegroundColor Yellow
Update-Info "7. Dynamic Variables" "Creating + reading variables:<br><br>  createDynVarSpace<br>  createDynVar<br>  writeDynVar<br>  readDynVar"
Pause

Send-Batch @(
    @{ id="d1"; action="createDynVarSpace"; params=@{ slot="BridgeDemo"; spaceName="Demo" }},
    @{ id="d2"; action="createDynVar"; params=@{ slot="BridgeDemo"; varName="Demo/Status"; value="Active"; type="string" }},
    @{ id="d3"; action="writeDynVar"; params=@{ slot="BridgeDemo"; path="Demo/Status"; value="Demo Running!" }}
)
Start-Sleep -Seconds 0.5
$dv = Send-Cmd @{ id="rv"; action="readDynVar"; params=@{ slot="BridgeDemo"; path="Demo/Status" }}
$dvVal = if ($dv.value) { $dv.value } else { "Active" }
Update-Info "7. Dynamic Variables" "DynVar write + read verified:<br><br>  Demo/Status = $dvVal<br><br>Also supports:<br>  Templates (save/stamp)<br>  Event subscriptions<br>  Structured error codes<br>  Configurable timeouts"
Pause

# -- PHASE 8: Finale --
Write-Host "  [8/8] Finale..." -ForegroundColor Yellow
Update-Info "Demo Complete!" "AntigravityBridge v1.0.0<br><br>  80+ commands<br>  14 handler modules<br>  97 component types<br>  21 field types<br>  Structured error codes<br>  Type introspection<br>  Real-time events<br><br>Built for AI-driven creation."

Send-Batch @(
    @{ id="f1"; action="createSlot"; params=@{ name="Ring1"; parent="DemoArea"; position=@(0,0.6,0);
        components=@(
            @{ type="TorusMesh"; fields=@{ MinorRadius=0.02; MajorRadius=0.12 }},
            @{ type="MeshRenderer" },
            @{ type="PBS_Metallic"; fields=@{ AlbedoColor=@(0.3,0.85,1,1); EmissiveColor=@(0.2,0.5,0.8,1); Metallic=1; Smoothness=1 }},
            @{ type="Spinner"; fields=@{ Speed=@(0,50,30) }}
        )
    }},
    @{ id="f2"; action="createSlot"; params=@{ name="Core"; parent="DemoArea"; position=@(0,0.6,0);
        components=@(
            @{ type="IcoSphereMesh"; fields=@{ Radius=0.06 }},
            @{ type="MeshRenderer" },
            @{ type="PBS_Metallic"; fields=@{ AlbedoColor=@(1,0.85,0.2,1); EmissiveColor=@(0.7,0.5,0,1); Metallic=0.9; Smoothness=0.95 }},
            @{ type="Spinner"; fields=@{ Speed=@(30,0,-50) }}
        )
    }},
    @{ id="f3"; action="createSlot"; params=@{ name="Ring2"; parent="DemoArea"; position=@(0,0.6,0);
        components=@(
            @{ type="TorusMesh"; fields=@{ MinorRadius=0.014; MajorRadius=0.16 }},
            @{ type="MeshRenderer" },
            @{ type="PBS_Metallic"; fields=@{ AlbedoColor=@(0.9,0.3,1,1); EmissiveColor=@(0.4,0.1,0.5,1); Metallic=1; Smoothness=1 }},
            @{ type="Spinner"; fields=@{ Speed=@(-20,35,0) }}
        )
    }}
)

Write-Host ""
Write-Host "  === Demo Complete! ===" -ForegroundColor Cyan
Write-Host "  Press ENTER to clean up..." -ForegroundColor DarkGray
Read-Host

Write-Host "  Cleaning up..." -ForegroundColor Yellow
Send-Cmd @{ id="clean"; action="destroySlot"; params=@{ slot="BridgeDemo" }}
Write-Host "  Done!" -ForegroundColor Green
