﻿#version 330

in vec3 FragPos;
in vec3 N;
in vec2 UV0;

uniform mat4 mvp;
uniform sampler2D dif;
uniform int hasDif;

out vec4 fragColor;

void main()
{
	vec2 TexCoord0 = UV0;

	vec3 diffuseColor = vec3(0.5) + (N / 2);

	if(hasDif == 1)
		diffuseColor = texture2D(dif, TexCoord0).xyz;

	vec3 lightDir = vec3(0, 0, 1);

	float l = 0.6 + abs(dot(N, lightDir)) * 0.5;

	vec3 displayNormal = vec3(0.5) + (N / 2);

	diffuseColor.xyz *= l;

	fragColor = vec4(diffuseColor, 1);//vec4(, 1);
}