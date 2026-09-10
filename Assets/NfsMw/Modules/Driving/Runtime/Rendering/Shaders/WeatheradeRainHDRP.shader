// HDRP presentation adapter for the Weatherade SRS rain-drop texture.
// Weatherade stores the far and near opacity profiles in the red and green
// channels respectively. Its original renderer uses a Built-in-pipeline
// GrabPass, so this adapter keeps the same channel and distance behaviour in
// an HDRP unlit pass and translates its normal-map refraction to HDRP's
// distortion-vector pass.
Shader "NfsMwRemaster/Weatherade Rain HDRP"
{
    Properties
    {
        [MainTexture] _MainTex("Weatherade Rain Drop", 2D) = "white" {}
        [Normal] _Normal("Weatherade Rain Normal", 2D) = "bump" {}
        [MainColor] _Color("Tint", Color) = (0.929, 0.961, 1, 0.627)
        _NearBlurDistance("Near Profile Distance", Float) = 2
        _NearBlurFalloff("Near Profile Falloff", Float) = 1
        _OpacityFadeStartDistance("Camera Fade Start", Float) = 0.5
        _OpacityFadeFalloff("Camera Fade Falloff", Float) = 0.3
        _RefractionStrength("Refraction Strength", Range(0, 0.5)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "WeatheradeRain"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _NearBlurDistance;
                float _NearBlurFalloff;
                float _OpacityFadeStartDistance;
                float _OpacityFadeFalloff;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                output.color = input.color * _Color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 drop = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float cameraDistance = distance(_WorldSpaceCameraPos, input.positionWS);
                float nearBlend = 1.0 - smoothstep(
                    _NearBlurDistance,
                    _NearBlurDistance + max(0.001, _NearBlurFalloff),
                    cameraDistance);
                float opacityProfile = lerp(drop.r, drop.g, nearBlend);
                float cameraFade = smoothstep(
                    _OpacityFadeStartDistance,
                    _OpacityFadeStartDistance + max(0.001, _OpacityFadeFalloff),
                    cameraDistance);
                float alpha = saturate(opacityProfile * cameraFade * input.color.a);
                return float4(input.color.rgb, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DistortionVectors"
            Tags { "LightMode" = "DistortionVectors" }

            Stencil
            {
                WriteMask 2
                Ref 2
                Comp Always
                Pass Replace
            }

            Blend One One, One One
            BlendOp Add, Add
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_Normal);
            SAMPLER(sampler_Normal);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _NearBlurDistance;
                float _NearBlurFalloff;
                float _OpacityFadeStartDistance;
                float _OpacityFadeFalloff;
                float _RefractionStrength;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
                float cameraDistance : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;

                float cameraDistance = distance(_WorldSpaceCameraPos, positionWS);
                float cameraFade = smoothstep(
                    _OpacityFadeStartDistance,
                    _OpacityFadeStartDistance + max(0.001, _OpacityFadeFalloff),
                    cameraDistance);
                output.alpha = input.color.a * _Color.a * cameraFade;
                output.cameraDistance = cameraDistance;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 drop = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float nearBlend = 1.0 - smoothstep(
                    _NearBlurDistance,
                    _NearBlurDistance + max(0.001, _NearBlurFalloff),
                    input.cameraDistance);
                float alpha = saturate(lerp(drop.r, drop.g, nearBlend) * input.alpha);
                clip(alpha - 0.001);

                float3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_Normal, sampler_Normal, input.uv),
                    _RefractionStrength);
                return float4(normalTS.xy * alpha, 1.0, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
