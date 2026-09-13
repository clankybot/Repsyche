Shader "Custom/PSXAesthetic"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Saturation ("Saturation (0 = monochrome)", Range(0,1)) = 1
        _Contrast ("Contrast", Range(0.5,4)) = 1
        _Brightness ("Brightness", Range(0,2)) = 1
        _AmbientColor ("Ambient Color", Color) = (0.15,0.15,0.15,1)
        _SnapResolution ("Vertex Snap Resolution", Range(8,320)) = 100
        _TexScale ("Texture Scale (triplanar)", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                // Deliberately NOT perspective-corrected: this is what gives PS1-style
                // "affine" texture mapping, where the triplanar UVs derived from this swim
                // and warp across a triangle instead of interpolating correctly in 3D.
                noperspective float3 positionOSInterp : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half3 vertexLighting : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Saturation;
                half _Contrast;
                half _Brightness;
                half4 _AmbientColor;
                float _SnapResolution;
                float _TexScale;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                float4 clipPos = posInputs.positionCS;

                // --- PSX vertex snapping ---
                // The original PS1 GTE transformed vertices with limited fixed-point
                // precision, causing visible "wobble" as the camera moves. Emulate it by
                // snapping the clip-space position to a coarse grid in normalized device
                // coordinates (dividing/re-multiplying by w so the grid stays fixed in
                // screen space rather than warping with perspective).
                float w = clipPos.w;
                float2 ndc = clipPos.xy / w;
                ndc = floor(ndc * _SnapResolution) / _SnapResolution;
                clipPos.xy = ndc * w;
                OUT.positionHCS = clipPos;

                OUT.positionOSInterp = IN.positionOS.xyz;

                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.normalWS = normalWS;

                // --- Per-vertex (Gouraud) lighting, PS1 style ---
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                OUT.vertexLighting = _AmbientColor.rgb + mainLight.color * ndotl;

                return OUT;
            }

            // Hard (non-blended) axis-projected UV from object-space position. The meshes
            // driving this shader have no UV channel (destroyed by a voxel remesh during
            // asset generation), and a hard axis pick - rather than a smoothed triplanar
            // blend - also reads as chunkier and more period-appropriate.
            float2 TriplanarUV(float3 pos, float3 nrm, float scale)
            {
                float3 b = abs(nrm);
                if (b.x >= b.y && b.x >= b.z) return pos.yz * scale;
                if (b.y >= b.x && b.y >= b.z) return pos.xz * scale;
                return pos.xy * scale;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = TriplanarUV(IN.positionOSInterp, normalize(IN.normalWS), _TexScale);
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // Desaturate and push contrast on the TEXTURE's own luminance first, while it
                // still spans its natural (well-centered) range. Applying contrast after
                // multiplying in dim ambient/vertex lighting would pivot around a midpoint the
                // shaded color rarely reaches, crushing anything but the brightest-lit faces to
                // solid black instead of preserving a crisp high-contrast pattern.
                half luma = dot(tex.rgb, half3(0.299h, 0.587h, 0.114h));
                half3 texColor = lerp(luma.xxx, tex.rgb, _Saturation);
                texColor = saturate((texColor - 0.5h) * _Contrast + 0.5h);

                half3 color = texColor * _Color.rgb * IN.vertexLighting * _Brightness;

                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}