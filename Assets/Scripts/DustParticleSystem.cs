﻿/* 
Description:
    GPU Particles using compute shaders in Unity.
*/

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Dust 
{
    // Particle system buffer
    struct DustParticle
    {
        Vector3 pos;
        Vector3 vel;
        Color cd;
        float age;
        float lifespan;
        float mass;
        float momentum;
        float id; //for unique instancing
        Vector3 scale;
        Matrix4x4 rot;
    };

    public class DustParticleSystem : MonoBehaviour
    {
        #region Public Properties
        public ComputeShader ParticleSystemKernel;

        [Header("Particles")]
        public Vector2 Mass = new Vector2(0.5f, 0.5f);
        public Vector2 Momentum = new Vector2(0.95f, 0.95f);
        public Vector2 Lifespan = new Vector2(.5f, 1f);
        public Vector3 StartSize = new Vector3(1f,1f,1f);
        public Vector3 StartRotation = new Vector3(0f,0f,0f);
        public int PreWarmFrames = 0;

        [Header("Velocity")]
        public float InheritVelocity = 0f;
        public int EmitterVelocity = 0;
        public float GravityModifier = 0f;

        [Header("Shape")]
        public int Shape = 0;
        [Range(0,m_maxVertCount)]
        public int Emission = 65000;
        public float InitialSpeed = 0f;
        // [Range(0,1)]
        public float Jitter = 0f;
        [Range(0,1)]
        public float RandomizeDirection = 0f;
        public Vector3 EmissionSize = new Vector3(1f,1f,1f);
        [Range(0,1)]
        public float ScatterVolume = 0f;
        public MeshRenderer EmissionMeshRenderer;

        [Header("Rotation")]
        public bool AlignToDirection = false;
        public float RotationOverLifetime = 0f;

        [Header("Color")]
        [ColorUsageAttribute(true,true,0,8,.125f,3)]
        public Color StartColor = new Color(1f,1f,1f,1f);
        public ColorRamp ColorByLife;
        public ColorRampRange ColorByVelocity;

        [Header("Noise")]
        public int NoiseType = 1;
        public Vector3 NoiseAmplitude = new Vector3(0f,0f,0f);
        public Vector3 NoiseScale = new Vector3(1f,1f,1f);
        public Vector4 NoiseOffset = new Vector4(0f,0f,0f,0f);
        public Vector4 NoiseOffsetSpeed = new Vector4(0f,0f,0f,0f);
        #endregion

        #region Getters
        public ComputeBuffer ParticlesBuffer { get { return m_particlesBuffer; } }
        public int MaxVerts { get { return m_maxVertCount; } }
        #endregion
        
        #region Private Properties
        private int m_kernelSpawn;
        private int m_kernelUpdate;
        private ComputeBuffer m_particlesBuffer;
        private ComputeBuffer m_kernelArgs;
        private int[] m_kernelArgsLocal = new int[3];
    
        private Vector3 m_origin;
        private Vector3 m_initialVelocityDir;
        private Vector3 m_prevPos;

        private DustMeshEmitter m_meshEmitter;
        
        private const int m_maxVertCount = 1048576; //64*64*16*16 (Groups*ThreadsPerGroup)
        #endregion

        //We initialize the buffers and the material used to draw.
        void Start()
        {
            m_kernelSpawn = ParticleSystemKernel.FindKernel("DustParticleSpawn");
            m_kernelUpdate = ParticleSystemKernel.FindKernel("DustParticleUpdate");
            CreateBuffers();
            UpdateComputeUniforms();

            // Prewarm the system
            if (PreWarmFrames > 0) {
                for (int i = 0; i < PreWarmFrames; i++) {
                    Dispatch();
                }
            }
        }

        void FixedUpdate() 
        {
            UpdateComputeUniforms();
            Dispatch();
        }

        void OnDisable()
        {
            ReleaseBuffers();
        }

        private void Dispatch()
        {
            // if (m_kernelArgs == null) CreateBuffers();
            ParticleSystemKernel.DispatchIndirect(m_kernelSpawn, m_kernelArgs);
            ParticleSystemKernel.DispatchIndirect(m_kernelUpdate, m_kernelArgs);
        }

        private void UpdateComputeUniforms() 
        {
            // Update internal variables
            m_prevPos = transform.position;

            // Follow mouse cursor
            if (Input.GetMouseButton(0)){
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                Vector3 o = ray.origin + (ray.direction * 20f);
                transform.position = o;
            }

            // Handle where to take the initial velocity direction from
            // 0 = Rigidbody, 1 = Transform
            switch(EmitterVelocity) {
                case 0:
                    if (transform.parent != null) {
                        if (transform.parent.gameObject.GetComponent<Rigidbody>() != null) {
                            m_initialVelocityDir = transform.parent.gameObject.GetComponent<Rigidbody>().velocity;
                        }
                    }
                    else {
                        m_initialVelocityDir = Vector3.zero;
                    }
                    break;
                case 1:
                    m_initialVelocityDir = transform.position-m_prevPos;
                    break;
            }

            m_origin = transform.position;

            ParticleSystemKernel.SetFloat("dt", Time.fixedDeltaTime);
            ParticleSystemKernel.SetFloat("fixedTime", Time.fixedTime);
            // Particles
            ParticleSystemKernel.SetVector("origin", m_origin);
            ParticleSystemKernel.SetVector("massNew", Mass);
            ParticleSystemKernel.SetVector("momentumNew", Momentum);
            ParticleSystemKernel.SetVector("lifespanNew", Lifespan);
			// Velocity
            ParticleSystemKernel.SetFloat("inheritVelocityMult", InheritVelocity);
            ParticleSystemKernel.SetVector("initialVelocityDir", m_initialVelocityDir);
            ParticleSystemKernel.SetVector("gravityIn", Physics.gravity);
            ParticleSystemKernel.SetFloat("gravityModifier", GravityModifier);
            ParticleSystemKernel.SetFloat("jitter", Jitter);
			// Shape
            ParticleSystemKernel.SetFloat("randomizeDirection", RandomizeDirection);
            ParticleSystemKernel.SetInt("emissionShape", Shape);
            ParticleSystemKernel.SetInt("emission", Emission);
            ParticleSystemKernel.SetVector("emissionSize", EmissionSize);
            ParticleSystemKernel.SetFloat("initialSpeed", InitialSpeed);
            ParticleSystemKernel.SetFloat("scatterVolume", ScatterVolume);
			// Rotation
            ParticleSystemKernel.SetBool("alignToDirection", AlignToDirection);
            ParticleSystemKernel.SetVector("startSize", StartSize);
            ParticleSystemKernel.SetVector("startRotation", StartRotation);
            ParticleSystemKernel.SetFloat("rotationOverLifetime", RotationOverLifetime);
			// Color
            ParticleSystemKernel.SetVector("startColor", StartColor);
            ParticleSystemKernel.SetFloat("velocityColorRange", ColorByVelocity.Range);
			// Noise
            ParticleSystemKernel.SetInt("noiseType", NoiseType);
            ParticleSystemKernel.SetVector("noiseAmplitude", NoiseAmplitude);
            ParticleSystemKernel.SetVector("noiseScale", NoiseScale);
            ParticleSystemKernel.SetVector("noiseOffset", NoiseOffset);
            ParticleSystemKernel.SetVector("noiseOffsetSpeed", NoiseOffsetSpeed);
            if (m_meshEmitter != null) {
                ParticleSystemKernel.SetMatrix("emissionMeshMatrix", m_meshEmitter.MeshRenderer.localToWorldMatrix);
                ParticleSystemKernel.SetMatrix("emissionMeshMatrixInvT", m_meshEmitter.MeshRenderer.localToWorldMatrix.inverse.transpose);
                ParticleSystemKernel.SetInt("emissionMeshVertCount", m_meshEmitter.VertexCount);
                ParticleSystemKernel.SetInt("emissionMeshTrisCount", m_meshEmitter.TriangleCount);
            }
        }


        // Create and initialize compute shader buffers
        private void CreateBuffers()
        {
            // Allocate
            m_kernelArgs = new ComputeBuffer(3, sizeof(int), ComputeBufferType.IndirectArguments);
            m_particlesBuffer = new ComputeBuffer(m_maxVertCount, Marshal.SizeOf(typeof(DustParticle))); //float3 pos, vel, cd; float age

            UpdateKernelArgs();
            ParticleSystemKernel.SetBuffer(m_kernelSpawn, "kernelArgs", m_kernelArgs);
            ParticleSystemKernel.SetBuffer(m_kernelUpdate, "kernelArgs", m_kernelArgs);

            DustParticle[] particlesTemp = new DustParticle[m_maxVertCount];
            for (int i = 0; i < m_maxVertCount; i++) {
                particlesTemp[i] = new DustParticle();
            }

            m_particlesBuffer.SetData(particlesTemp);
            ParticleSystemKernel.SetBuffer(m_kernelSpawn, "output", m_particlesBuffer);
            ParticleSystemKernel.SetBuffer(m_kernelUpdate, "output", m_particlesBuffer);

            // Create color ramp textures
            ColorByLife.Setup();
            ColorByVelocity.Setup();
            ParticleSystemKernel.SetTexture(m_kernelUpdate, "_colorByLife", (Texture)ColorByLife.Texture);
            ParticleSystemKernel.SetTexture(m_kernelUpdate, "_colorByVelocity", (Texture)ColorByVelocity.Texture);

            // Set up mesh emitter
            if (EmissionMeshRenderer != null) {
                m_meshEmitter = new DustMeshEmitter(EmissionMeshRenderer);
                m_meshEmitter.Update();
                
                ParticleSystemKernel.SetBuffer(m_kernelSpawn, "emissionMesh", m_meshEmitter.MeshBuffer);
                ParticleSystemKernel.SetBuffer(m_kernelSpawn, "emissionMeshTris", m_meshEmitter.MeshTrisBuffer);
            }

        }

        public void UpdateKernelArgs()
        {
            int groupSize = (int)Mathf.Ceil(Mathf.Sqrt(Emission)/16f);
            if (m_kernelArgsLocal.Length < 3) m_kernelArgsLocal = new int[3];

            m_kernelArgsLocal[0] = groupSize;
            m_kernelArgsLocal[1] = groupSize;
            m_kernelArgsLocal[2] = 1;

            m_kernelArgs.SetData(m_kernelArgsLocal);
        }

        private void ReleaseBuffers()
        {
            m_particlesBuffer.Release();
            m_kernelArgs.Release();
            if (m_meshEmitter != null) {
                m_meshEmitter.ReleaseBuffers();
            }
        }

    }
}