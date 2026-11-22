Shader "Chipi/Contact Shadow"
{
    SubShader
    {
        Tags { "Queue" = "Transparent-20" "RenderType" = "Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 offset = input.uv * 2.0 - 1.0;
                float distance = length(offset);
                float opacity = (1.0 - smoothstep(0.08, 1.0, distance)) * 0.29;
                return fixed4(0.16, 0.28, 0.43, opacity);
            }
            ENDCG
        }
    }
}
