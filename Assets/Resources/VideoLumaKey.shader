Shader "Hidden/VideoLumaKey"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Threshold ("Luma Threshold", Range(0, 0.5)) = 0.12
        _Softness ("Edge Softness", Range(0.01, 0.3)) = 0.08
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float _Threshold;
            float _Softness;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                // Luminance (Rec. 709)
                float luma = dot(col.rgb, float3(0.2126, 0.7152, 0.0722));
                // Smooth step : pixels sombres → transparent
                float alpha = smoothstep(_Threshold, _Threshold + _Softness, luma);
                col.a = alpha;
                return col;
            }
            ENDCG
        }
    }
}
