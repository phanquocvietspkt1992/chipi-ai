Shader "Chipi/Concept Projection"
{
    Properties
    {
        _MainTex ("Approved concept", 2D) = "white" {}
        _Blink ("Blink", Range(0, 1)) = 0
        _Speaking ("Speaking", Float) = 0
        _ChestGlow ("Chest glow", Float) = 0
        _MouthRound ("Speech rounding", Range(0, 1)) = 0
        _JawOpen ("Speech opening", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _JawOpen;
            float _MouthRound;
            float _Blink;
            float _Speaking;
            float _ChestGlow;

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 worldPosition : TEXCOORD2;
            };

            Varyings vert(appdata_base input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord.xy;
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            float3 ApplyBlink(float3 color, float2 uv, float2 center)
            {
                float2 offset = (uv - center) / float2(0.063, 0.069);
                float coverage = 1.0 - smoothstep(0.69, 1.0, length(offset));
                float2 skinUV = center + float2(offset.x * 0.036, 0.079 + offset.y * 0.015);
                float3 lid = tex2D(_MainTex, skinUV).rgb;
                float lash = 1.0 - smoothstep(0.035, 0.13,
                    abs(offset.y + 0.17 - 0.24 * offset.x * offset.x));
                lid = lerp(lid, float3(0.25, 0.15, 0.13), lash * 0.67);
                float upperLid = smoothstep(-0.17, 0.15, offset.y + 1.35 * _Blink - 0.69);
                return lerp(color, lid, coverage * upperLid * smoothstep(0.12, 0.84, _Blink));
            }

            fixed4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 pivot = float2(261.0 / 512.0, 1.0 - 232.0 / 512.0);
                float2 offset = uv - pivot;
                float influence = 1.0 - smoothstep(0.45, 1.0,
                    length(offset / float2(0.066, 0.056)));
                float mouthHeight = lerp(1.0, lerp(0.56, 1.65, _JawOpen), _Speaking);
                float mouthWidth = lerp(1.0, lerp(1.10, 0.68, _MouthRound), _Speaking);
                uv = pivot + offset / lerp(float2(1.0, 1.0), float2(mouthWidth, mouthHeight), influence);
                fixed4 color = tex2D(_MainTex, uv);
                if (_Blink > 0.16)
                {
                    color.rgb = ApplyBlink(color.rgb, input.uv, float2(205.0, 331.0) / 512.0);
                    color.rgb = ApplyBlink(color.rgb, input.uv, float2(314.0, 331.0) / 512.0);
                }
                float3 normal = normalize(input.normal);
                float3 view = normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                float3 key = normalize(float3(-0.56, 0.70, -0.44));
                float keyAmount = saturate(dot(normal, key));
                float facing = saturate(dot(normal, view));
                float highlight = pow(saturate(dot(normal, normalize(key + view))), 20.0);
                float rim = pow(1.0 - facing, 2.4) * saturate(normal.y + normal.x);

                // Keep the approved skin tones and painted eyes closer to the image.
                float2 face = (input.uv - float2(0.502, 0.631)) / float2(0.209, 0.177);
                float armor = smoothstep(0.78, 1.08, length(face));
                color.rgb *= lerp(0.92 + 0.12 * keyAmount, 0.74 + 0.31 * keyAmount, armor);
                color.rgb += armor * (highlight * 0.10 + rim * 0.09) * float3(0.72, 0.88, 1.0);
                float2 display = (input.uv - float2(257.0, 191.0) / 512.0) / float2(0.039, 0.023);
                float displayMask = 1.0 - smoothstep(0.38, 1.0, length(display));
                color.rgb += float3(0.06, 0.35, 0.42) * (_ChestGlow * displayMask);
                return color;
            }
            ENDCG
        }
    }
}
