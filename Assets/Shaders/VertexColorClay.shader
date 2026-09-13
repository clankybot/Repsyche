Shader "Custom/VertexColorClay"
{
    // Renders a mesh purely from its per-vertex color attribute (no texture), with
    // simple per-vertex (Gouraud) lighting and an optional saturation boost. This is
    // the companion shader for procedurally generated meshes that carry color only as
    // vertex data - e.g. Tools/Blender/generate_procedural_island.py - since the
    // default URP/Lit shader ignores vertex color entirely and renders them flat gray.
    Properties
    {
        _Saturation ("Saturation Boost", Range(0,3)) = 1.3
        _Brightness ("Brightness", Range(0,2)) = 1
        _AmbientColor ("Ambient Color", Color) = (0.25,0.25,0.3,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            // Off, not Back: procedurally generated meshes (especially a large open
            // grid like DesertGround) can end up with normals facing either way -
            // Blender's normal-recalculation heuristic has no reliable "outward"
            // reference for a non-manifold sheet. Culling Back made the mesh solid to
            // the MeshCollider but invisible from above whenever normals pointed down.
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS      : TEXCOORD0;
                half4 vertexColor    : TEXCOORD1;
                half3 vertexLighting : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half _Saturation;
                half _Brightness;
                half4 _AmbientColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;

                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.normalWS = normalWS;
                OUT.vertexColor = IN.color;

                // Same per-vertex Gouraud lighting approach as PSXAesthetic.shader, so
                // procedurally-colored props sit visually consistent with the rest of
                // the scene's low-fi lighting.
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                OUT.vertexLighting = _AmbientColor.rgb + mainLight.color * ndotl;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half luma = dot(IN.vertexColor.rgb, half3(0.299h, 0.587h, 0.114h));
                half3 satColor = saturate(lerp(luma.xxx, IN.vertexColor.rgb, _Saturation));

                half3 color = satColor * IN.vertexLighting * _Brightness;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
