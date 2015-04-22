﻿using UnityEngine;
using System.Collections;
using System.IO;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using KSP.IO;








namespace scatterer
{
	/*
	 * Loads the tables required for the atmospheric scattering and sets any uniforms for shaders
	 * that need them. If you create new tables using the PreprocessAtmo.cs script and changed some of  
	 * the settings (like the tables dimensions) you need to make sure the settings match here.
	 * You can adjust some of these settings (mieG, betaR) to change the look of the scattering but
	 * as precomputed tables are used there is a limit to how much the scattering will change.
	 * For large changes you will need to create new table with the settings you want.
	 * NOTE - all scenes must contain a skyNode
	 */
	public class SkyNode : MonoBehaviour 
	{

		PluginConfiguration cfg = KSP.IO.PluginConfiguration.CreateForType<SkyNode>(null);

		//bool inScaledSpace=false;

		CelestialBody parentCelestialBody;
		Matrix4x4 m_sun_worldToLocalRotation;

		bool initiatedSky=false;
		MeshRenderer m_meshRenderer;
		GameObject atmosphere;
		bool sunglareEnabled=true;
		float sunglareCutoffAlt;

		Texture2D sunGlare;
		Texture2D black;


		float inscatteringCoeff=0.8f;
		float extinctionCoeff=0.3f;

		float atmosphereGlobalScale=1f;
		float postProcessingAlpha=0.95f;
		float postProcessingScale=1f;
		float postProcessDepth=0.02f;
		float postProcessExposure=0.18f;

		static PQS CurrentPQS=null;		
		static bool inScaledSpace { get { return !(CurrentPQS != null && CurrentPQS.isActive); } }


		Vector3 position;

		bool initiated=false;
		Camera[] cams;
		Camera farCamera, scaledSpaceCamera;
		bool postprocessingEnabled=true;
		int waitBeforeReloadCnt=0;
		GameObject idek=new GameObject();
//		MeshFilter MF;
//		MeshRenderer mr;
		float alphaCutoff=0.001f;
		float alphaGlobal=1f;
		//float m_radius = 63600.0f*4;
		float m_radius;// = 600000.0f;
		//The radius of the planet (Rg), radius of the atmosphere (Rt)
		//const float Rg = 63600.0f*4;
		float Rg;// = 600000.0f;
		float Rt;// = (64200f/63600f) * 600000.0f;
		float RL;// = (64210.0f/63600f) * 600000.0f;
		
		//Dimensions of the tables
		const int TRANSMITTANCE_W = 256;
		const int TRANSMITTANCE_H = 64;
		const int SKY_W = 64;
		const int SKY_H = 16;
		const int RES_R = 32;
		const int RES_MU = 128;
		const int RES_MU_S = 32;
		const int RES_NU = 8;

		int layer=15;
		int cam=1;
		
		float AVERAGE_GROUND_REFLECTANCE = 0.1f;
		//Half heights for the atmosphere air density (HR) and particle density (HM)
		//This is the height in km that half the particles are found below
		float HR = 8.0f;
		float HM = 1.2f;
		//scatter coefficient for mie
		Vector3 BETA_MSca = new Vector3(4e-3f,4e-3f,4e-3f);

		public Material m_atmosphereMaterial;

		[SerializeField]
		Material m_skyMaterial;
		
		[SerializeField]
		Material m_skyMapMaterial;
		//scatter coefficient for rayliegh
		[SerializeField]
		Vector3 m_betaR = new Vector3(5.8e-3f, 1.35e-2f, 3.31e-2f);
		//Asymmetry factor for the mie phase function
		//A higher number meands more light is scattered in the forward direction
		[SerializeField]
		float m_mieG = 0.85f;
		
		string m_filePath = "/Proland/Textures/Atmo";
		
		Mesh m_mesh;
		
		RenderTexture m_transmit, m_inscatter, m_irradiance;//, m_skyMap;//, m_inscatterGround, m_transmitGround;

		Manager m_manager;


		public void loadSettings()
		{
			cfg.load ();
			Rg =float.Parse(cfg.GetValue<string>("Rg"));
			Rt =float.Parse(cfg.GetValue<string>("Rt"));
			RL =float.Parse(cfg.GetValue<string>("RL"));
			
			m_betaR = cfg.GetValue<Vector3>("BETA_R");
			BETA_MSca = cfg.GetValue<Vector3>("BETA_MSca");
			m_mieG =float.Parse(cfg.GetValue<string>("MIE_G"));
			
			HR =float.Parse( cfg.GetValue<string>("HR"));
			HM =float.Parse( cfg.GetValue<string>("HM"));
			AVERAGE_GROUND_REFLECTANCE =float.Parse(cfg.GetValue<string>("AVERAGE_GROUND_REFLECTANCE"));
			atmosphereGlobalScale=float.Parse(cfg.GetValue<string>("atmosphereGlobalScale"));

		}

		public void saveSettings()
		{
			cfg ["Rg"] = Rg.ToString();
			cfg ["Rt"] = Rt.ToString();
			cfg ["RL"] = RL.ToString();

			cfg ["BETA_R"] = m_betaR;
			cfg ["BETA_MSca"] = BETA_MSca;
			cfg ["MIE_G"] = m_mieG.ToString();
			cfg ["HR"] = HR.ToString();
			cfg ["HM"] = HM.ToString();
			cfg ["AVERAGE_GROUND_REFLECTANCE"] = AVERAGE_GROUND_REFLECTANCE.ToString();

			cfg.save ();
			
		}
		
		// Use this for initialization
		public void Start() 
		{

			//Component.Destroy(cams[cam].gameObject.GetComponent<scatterPostprocess>());
			m_radius=m_manager.GetRadius();
			//m_radius = 600000.0f;

//			Rt = (64200f / 63600f) * m_radius;
//			RL = (64210.0f/63600f) * m_radius;
			Rt = (Rt / Rg) * m_radius;
			RL = (RL / Rg) * m_radius;
			Rg = m_radius;

//			m_mesh = MeshFactory.MakePlane(2, 2, MeshFactory.PLANE.XY, false,false);
//			m_mesh.bounds = new Bounds(m_manager.getParentCelestialBody().transform.position, new Vector3(1e8f,1e8f, 1e8f));
			m_mesh = isoSphere.Create ();

			//The sky map is used to create a reflection of the sky for objects that need it (like the ocean)
			//We don't need the skymap here, this is from the proland code
		/*	m_skyMap = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGBHalf);
			m_skyMap.filterMode = FilterMode.Trilinear;
			m_skyMap.wrapMode = TextureWrapMode.Clamp;
			m_skyMap.anisoLevel = 9;
			m_skyMap.useMipMap = true;
			//m_skyMap.mipMapBias = -0.5f;
			m_skyMap.Create();*/
			

			//Inscatter is responsible for the change in the sky color as the sun moves
			//The raw file is a 4D array of 32 bit floats with a range of 0 to 1.589844
			//As there is not such thing as a 4D texture the data is packed into a 3D texture 
			//and the shader manually performs the sample for the 4th dimension
			m_inscatter = new RenderTexture(RES_MU_S * RES_NU, RES_MU * RES_R, 0, RenderTextureFormat.ARGBHalf);

			m_inscatter.wrapMode = TextureWrapMode.Clamp;
			m_inscatter.filterMode = FilterMode.Bilinear;




			//Transmittance is responsible for the change in the sun color as it moves
			//The raw file is a 2D array of 32 bit floats with a range of 0 to 1									
			m_transmit = new RenderTexture(TRANSMITTANCE_W, TRANSMITTANCE_H, 0, RenderTextureFormat.ARGBHalf);
			m_transmit.wrapMode = TextureWrapMode.Clamp;
			m_transmit.filterMode = FilterMode.Bilinear;
									
			//Iirradiance is responsible for the change in the sky color as the sun moves
			//The raw file is a 2D array of 32 bit floats with a range of 0 to 1					
			m_irradiance = new RenderTexture(SKY_W, SKY_H, 0, RenderTextureFormat.ARGBHalf);
			m_irradiance.wrapMode = TextureWrapMode.Clamp;
			m_irradiance.filterMode = FilterMode.Bilinear;



			initiateOrRestart ();			

			m_skyMaterial=new Material(ShaderTool.GetMatFromShader2("CompiledSky.shader"));


			sunGlare = new Texture2D (512, 512);
			black = new Texture2D (512, 512);

			string codeBase = Assembly.GetExecutingAssembly().CodeBase;
			UriBuilder uri = new UriBuilder(codeBase);
			string path = Uri.UnescapeDataString(uri.Path);
			path=Path.GetDirectoryName (path);

			sunGlare.LoadImage(System.IO.File.ReadAllBytes(String.Format("{0}/{1}", path + m_filePath, "sunglare.png")));
			black.LoadImage(System.IO.File.ReadAllBytes(String.Format("{0}/{1}", path + m_filePath, "black.png")));

			if (sunGlare == null) {
				print ("SUNGLARE NULL");

			} else {
				sunGlare.wrapMode = TextureWrapMode.Clamp;
				m_skyMaterial.SetTexture("_Sun_Glare", sunGlare);

			}

						
			InitUniforms(m_skyMaterial);
			m_atmosphereMaterial = ShaderTool.GetMatFromShader2 ("CompiledAtmosphericScatter.shader");

			//aniso defaults to to forceEnable on higher visual settings and causes artifacts
			QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;

			CurrentPQS = parentCelestialBody.pqsController;	

//			atmosphere=new GameObject();
//
//			MeshFilter m_meshFilter=atmosphere.AddComponent<MeshFilter>();
//			m_meshFilter.mesh.Clear ();
//			m_meshFilter.mesh = m_mesh;
//
//			m_meshRenderer = atmosphere.AddComponent<MeshRenderer>();
//
//
//			m_skyMaterial.renderQueue = 1000;
//
//			m_meshRenderer.sharedMaterial = m_skyMaterial;
//			m_meshRenderer.castShadows = false;
//			m_meshRenderer.receiveShadows = false;
//			m_meshRenderer.enabled = true;
//
//
//			atmosphere.layer = 15;
//			atmosphere.transform.parent = m_manager.getParentCelestialBody().transform;
//			atmosphere.transform.localPosition = Vector3.zero;
//			atmosphere.transform.localRotation = Quaternion.identity;

		}




		public void initiateOrRestart(){

			m_inscatter.Create ();
			m_transmit.Create ();
			m_irradiance.Create ();

			string codeBase = Assembly.GetExecutingAssembly().CodeBase;
			UriBuilder uri = new UriBuilder(codeBase);
			string path = Uri.UnescapeDataString(uri.Path);
			path=Path.GetDirectoryName (path);

			
			string path1 = path + m_filePath + "/transmittance.raw";
			EncodeFloat.WriteIntoRenderTexture (m_transmit, 3, path1,null);

			path1 = path + m_filePath + "/irradiance.raw";
			EncodeFloat.WriteIntoRenderTexture (m_irradiance, 3, path1,null);

			path1 = path + m_filePath + "/inscatter.raw";
			EncodeFloat.WriteIntoRenderTexture (m_inscatter, 4, path1,null);
		}

		public void SetInscatteringCoeff(float inCoeff) {
			inscatteringCoeff = inCoeff;
		}

		public void SetExtinctionCoeff(float exCoeff) {
			extinctionCoeff = exCoeff;
		}



		public void SetRadius(float rad) {
			m_radius=rad;
		}

		public void SetAlphaCutoff(float cutoff) {
			alphaCutoff = cutoff;
		}

		public void SetAlphaGlobal(float glob) {
			alphaGlobal = glob;
		}
		
		public void OnDestroy()
		{
			//base.OnDestroy();
			
			m_transmit.Release();
			m_irradiance.Release();
			m_inscatter.Release();

			scatterPostprocess tmp = farCamera.gameObject.GetComponent<scatterPostprocess> ();
			
			if(tmp != null) 
			{
				Component.Destroy (tmp);
			}

//			m_inscatterGround.Release();
//			m_transmitGround.Release();
			//m_skyMap.Release();
		}
		

		public void setLayernCam(int inLayer, int inCam)
		{
			layer = inLayer;
			cam = inCam;
		}

		// Update is called once per frame
		public void UpdateNode() 
		{



//			if (initiated)
//			Graphics.DrawMesh (m_mesh,position, new Quaternion (0, 1, 0, 0), m_skyMaterial, layer, cams [cam]);


			m_radius=m_manager.GetRadius();
			//m_radius = 600000.0f;

			Rt = (Rt / Rg) * m_radius;
			RL = (RL / Rg) * m_radius;
			Rg = m_radius;
			sunglareCutoffAlt = Rt * 0.995f * atmosphereGlobalScale;

			//			if(inScaledSpace)
			//			{
			//				position=(ScaledSpace.Instance.scaledSpaceTransforms.Single(t => t.name == parentCelestialBody.name)).position;
			//			}
			//			else{
			position= parentCelestialBody.transform.position;
			//			}
			
			//			print ("In scaled Space");
			//			print (inScaledSpace);
			

			if (!initiated) { //gets the cameras, this isn't done at start() because the cameras still don't exist then and it crashes the game
				cams = Camera.allCameras;
								
				for (int i=0; i<cams.Length; i++) 
				{
					if (cams [i].name == "Camera Scaled Space")
						scaledSpaceCamera = cams [i];
					if (cams [i].name == "Camera 01")
						farCamera = cams [i];				
				}
				scatterPostprocess tmp = farCamera.gameObject.GetComponent<scatterPostprocess> ();

				if(tmp!= null) 
				{
					Component.Destroy (tmp);
				}
				
				if (postprocessingEnabled) {
					farCamera.gameObject.AddComponent (typeof(scatterPostprocess));
					if(farCamera.gameObject.GetComponent<scatterPostprocess> () != null)
					{
						initiated =true;
					}
				}

				else
				{				
				initiated =true;
				}
			}

			//m_mesh.bounds = new Bounds (position, new Vector3 (1e8f, 1e8f, 1e8f));
			
			m_skyMaterial.SetFloat ("_Alpha_Cutoff", alphaCutoff);
			m_skyMaterial.SetFloat ("_Alpha_Global", alphaGlobal);
			




			float alt = Vector3.Distance(farCamera.transform.position, parentCelestialBody.transform.position);
			if ((sunglareEnabled)^(alt < sunglareCutoffAlt)) //^ is XOR
			{
				toggleSunglare();
			}


			//if alt-tabbing/windowing and textures are lost

				if (!m_inscatter.IsCreated ()) {
				waitBeforeReloadCnt++;
				if (waitBeforeReloadCnt>=2){

					initiateOrRestart ();														
					print ("Scatterer: reloaded scattering tables");
					waitBeforeReloadCnt=0;
				}
			}





			if (postprocessingEnabled) {	
				InitPostprocessMaterial(m_atmosphereMaterial);			 	
				UpdatePostProcessMaterial (m_atmosphereMaterial);
				
				if(farCamera.gameObject.GetComponent<scatterPostprocess> () == null) 
				{
					farCamera.gameObject.AddComponent (typeof(scatterPostprocess));
				}

				farCamera.gameObject.GetComponent<scatterPostprocess>().setMaterial(m_atmosphereMaterial);
			}

//			m_manager.SetUniforms (m_skyMaterial);
//			m_skyMaterial.SetMatrix ("_Sun_WorldToLocal", m_manager.GetSunWorldToLocalRotation ()); //don't touch this
//			SetUniforms (m_skyMaterial);
//
//			//Mesh m_mesh = new Mesh();
//
//
//				
//			if (m_mesh == null) 
//				m_mesh = isoSphere.Create ();
//
//				m_mesh.bounds = new Bounds (position, new Vector3 (1e8f, 1e8f, 1e8f));
//			
//
//			Graphics.DrawMesh (m_mesh, position, new Quaternion (0, 1, 0, 0), m_skyMaterial, layer, cams [cam]);

			//Destroy (m_mesh);
		
//			m_manager.SetUniforms (m_skyMaterial);
//			m_skyMaterial.SetMatrix ("_Sun_WorldToLocal", m_manager.GetSunWorldToLocalRotation ()); //don't touch this
//			SetUniforms (m_skyMaterial);


//			Quaternion q = Quaternion.FromToRotation(m_manager.getDirectionToSun(), new Vector3(0,0,1));
//			m_sun_worldToLocalRotation = Matrix4x4.TRS(Vector3.zero, q, Vector3.one);
//			m_skyMaterial.SetMatrix ("_Sun_WorldToLocal", m_sun_worldToLocalRotation);
//
//
//			//copied from m_manager's set uniforms
//			m_skyMaterial.SetMatrix ("_Globals_WorldToCamera", farCamera.worldToCameraMatrix);
//			m_skyMaterial.SetMatrix ("_Globals_CameraToWorld", farCamera.worldToCameraMatrix.inverse);
//			
//			Matrix4x4 p = farCamera.projectionMatrix;
//			Matrix4x4d m_cameraToScreenMatrix = new Matrix4x4d (p);
//			
//
//				
//				m_skyMaterial.SetMatrix ("_Globals_CameraToScreen", m_cameraToScreenMatrix.ToMatrix4x4 ());
//				m_skyMaterial.SetMatrix ("_Globals_ScreenToCamera", m_cameraToScreenMatrix.Inverse ().ToMatrix4x4 ());
////
////				
////				m_skyMaterial.SetMatrix ("_Globals_CameraToScreen", m_cameraToScreenMatrix.Inverse ().ToMatrix4x4 ());
////				m_skyMaterial.SetMatrix ("_Globals_ScreenToCamera", m_cameraToScreenMatrix.ToMatrix4x4 ());
////
//				m_skyMaterial.SetVector ("_Globals_WorldCameraPos", farCamera.transform.position);
//				m_skyMaterial.SetVector ("_Globals_Origin", Vector3.zero-parentCelestialBody.transform.position);	
//

			//m_skyMaterial.renderQueue = farCamera.gameObject.GetComponent<scatterPostprocess> ().m_atmosphereImageEffect.renderQueue;


//			if (initiated) {
//				//cams [cam].Render ();
//				Graphics.DrawMesh (m_mesh, position, new Quaternion (0, 1, 0, 0), m_skyMaterial, layer, cams [cam]);
//
//			
//			}



			if (farCamera.gameObject.GetComponent<drawSky> () == null) {
				farCamera.gameObject.AddComponent (typeof(drawSky));

				initiatedSky=true;
			}

			if (farCamera.gameObject.GetComponent<drawSky> () != null) 
			{
				farCamera.gameObject.GetComponent<drawSky> ().settings(m_skyMaterial,position,m_mesh, m_manager,this,farCamera, layer);
			}


		}

		public void drawSky()
		{
//			m_manager.SetUniforms (m_skyMaterial);
//			m_skyMaterial.SetMatrix ("_Sun_WorldToLocal", m_manager.GetSunWorldToLocalRotation ()); //don't touch this
//			SetUniforms (m_skyMaterial);
//			
//			//Mesh m_mesh = new Mesh();
//			
//			
//			Mesh m_mesh;
//
////			if (m_mesh == null) 
//				m_mesh = isoSphere.Create ();
//			
//			m_mesh.bounds = new Bounds (position, new Vector3 (1e8f, 1e8f, 1e8f));
//			
//			
//			Graphics.DrawMesh (m_mesh, position, new Quaternion (0, 1, 0, 0), m_skyMaterial, layer, cams [cam]);


		}


		public void SetUniforms(Material mat)
		{	
			//Sets uniforms that this or other gameobjects may need
			if(mat == null) return;
			//mat.SetFloat ("atmosphereGlobalScale", atmosphereGlobalScale);
			mat.SetFloat("scale",atmosphereGlobalScale);
			mat.SetFloat("Rg", Rg*atmosphereGlobalScale);
			mat.SetFloat("Rt", Rt*atmosphereGlobalScale);
			mat.SetFloat("RL", RL*atmosphereGlobalScale);

			mat.SetMatrix ("_Globals_WorldToCamera", farCamera.worldToCameraMatrix);
			mat.SetMatrix ("_Globals_CameraToWorld", farCamera.worldToCameraMatrix.inverse);



			mat.SetVector("betaR", m_betaR / 1000.0f);
			mat.SetFloat("mieG", Mathf.Clamp(m_mieG, 0.0f, 0.99f));
			mat.SetTexture("_Sky_Transmittance", m_transmit);
			mat.SetTexture("_Sky_Inscatter", m_inscatter);
			mat.SetTexture("_Sky_Irradiance", m_irradiance);
			//mat.SetTexture("_Sky_Map", m_skyMap);
			mat.SetFloat("_Sun_Intensity", 100f);		
			mat.SetVector("_Sun_WorldSunDir", m_manager.getDirectionToSun().normalized);
//			mat.SetVector("_Sun_WorldSunDir", m_manager.getDirectionToSun());

		}

		void InitPostprocessMaterial(Material mat)
		{
			

			mat.SetTexture("_Transmittance", m_transmit);
			mat.SetTexture("_Inscatter", m_inscatter);
			
			//Consts, best leave these alone
			mat.SetFloat("M_PI", Mathf.PI);

			//float SCALE = Rg / m_radius; //
			//float SCALE = 1 / 100;

			//mat.SetFloat("SCALE", SCALE);
			mat.SetFloat ("Rg", Rg*atmosphereGlobalScale*postProcessingScale);
			mat.SetFloat("Rt", Rt*atmosphereGlobalScale*postProcessingScale);
			mat.SetFloat("Rl", RL*atmosphereGlobalScale*postProcessingScale);
//			mat.SetFloat("RES_R", 32.0f);
//			mat.SetFloat("RES_MU", 128.0f);
//			mat.SetFloat("RES_MU_S", 32.0f);
//			mat.SetFloat("RES_NU", 8.0f);

			mat.SetFloat("RES_R", RES_R);
			mat.SetFloat("RES_MU", RES_MU);
			mat.SetFloat("RES_MU_S", RES_MU_S);
			mat.SetFloat("RES_NU", RES_NU);

			mat.SetFloat("SUN_INTENSITY", 100f);//

//			mat.SetVector("EARTH_POS", new Vector3(0.0f, 6360010.0f, 0.0f));
//			mat.SetVector("SUN_DIR", m_sun.transform.forward*-1.0f);


			//mat.SetVector("EARTH_POS", cams[cam].transform.position-m_manager.getParentCelestialBody().transform.position);
			//mat.SetVector("EARTH_POS", new Vector3(0.0f, 0.0f, 0.0f));
			mat.SetVector("_inCamPos", cams[cam].transform.position);
			//mat.SetVector("SUN_DIR", new Vector3(0.5f,0.5f,0.5f));
			mat.SetVector("SUN_DIR", m_manager.GetSunNodeDirection());

			
		}

		void UpdatePostProcessMaterial(Material mat)
		{	
			//mat.SetFloat ("atmosphereGlobalScale", atmosphereGlobalScale);
			mat.SetFloat ("Rg", Rg*atmosphereGlobalScale*postProcessingScale);
			mat.SetFloat("Rt", Rt*atmosphereGlobalScale*postProcessingScale);
			mat.SetFloat("Rl", RL*atmosphereGlobalScale*postProcessingScale);

			mat.SetFloat("_inscatteringCoeff", inscatteringCoeff);
			mat.SetFloat("_extinctionCoeff", extinctionCoeff);


			mat.SetFloat("_global_alpha", postProcessingAlpha);
			mat.SetFloat("_Exposure", postProcessExposure);
			mat.SetFloat("_global_depth", postProcessDepth);
//			mat.SetFloat("_global_depth", 1);
//			mat.SetFloat("_Scale", postProcessingScale);
			mat.SetFloat("_Scale", 1);

//			print ("SCALE");
//			print (postProcessingScale);


			mat.SetVector ("_Globals_Origin", /*Vector3.zero-*/parentCelestialBody.transform.position);	
			//uniform float3 _Globals_Origin;
//			mat.SetMatrix ("_Globals_CameraToWorld", cams [0].worldToCameraMatrix.inverse);
			mat.SetMatrix ("_Globals_CameraToWorld", cams [0].worldToCameraMatrix.inverse);
			mat.SetVector ("_CameraForwardDirection", cams [cam].transform.forward);
			//mat.SetVector("betaR", m_betaR / (Rg / m_radius));
//			mat.SetVector("betaR", m_betaR / (postProcessDepth));
			mat.SetVector("betaR", new Vector4(2.9e-3f, 0.675e-2f, 1.655e-2f, 0.0f));
			mat.SetFloat("mieG", 0.4f);
			mat.SetVector("SUN_DIR", /*Vector3.zero-*/m_manager.GetSunNodeDirection());
			mat.SetFloat("SUN_INTENSITY", 100f);
		}

		
		public void InitUniforms(Material mat)
		{
			//Init uniforms that this or other gameobjects may need
			if(mat == null) return;
			
			mat.SetFloat("scale",Rg*atmosphereGlobalScale /  m_radius);
			mat.SetFloat("Rg", Rg*atmosphereGlobalScale);
			mat.SetFloat("Rt", Rt*atmosphereGlobalScale);
			mat.SetFloat("RL", RL*atmosphereGlobalScale);

			mat.SetFloat("TRANSMITTANCE_W", TRANSMITTANCE_W);
			mat.SetFloat("TRANSMITTANCE_H", TRANSMITTANCE_H);
			mat.SetFloat("SKY_W", SKY_W);
			mat.SetFloat("SKY_H", SKY_H);
			mat.SetFloat("RES_R", RES_R);
			mat.SetFloat("RES_MU", RES_MU);
			mat.SetFloat("RES_MU_S", RES_MU_S);
			mat.SetFloat("RES_NU", RES_NU);
			mat.SetFloat("AVERAGE_GROUND_REFLECTANCE", AVERAGE_GROUND_REFLECTANCE);
			mat.SetFloat("HR", HR * 1000.0f);
			mat.SetFloat("HM", HM * 1000.0f);
			mat.SetVector("betaMSca", BETA_MSca / 1000.0f);
			mat.SetVector("betaMEx", (BETA_MSca / 1000.0f) / 0.9f);
			m_skyMaterial.SetFloat("_Alpha_Cutoff", alphaCutoff);
			
		}
		
		/*void OnGUI(){
			GUI.DrawTexture(new Rect(0,0,512, 512), m_skyMap);
		}*/
		
	
		public void SetNearPlane(int NR) {
			farCamera.gameObject.GetComponent<scatterPostprocess>().setNearPlane(NR);
		}
		
		
		
		public void SetFarPlane(int FR) {
			farCamera.gameObject.GetComponent<scatterPostprocess>().setFarPlane(FR);
		}


		public void setManager(Manager manager)
		{
			m_manager=manager;
		}

		public void enablePostprocess(){
			scatterPostprocess tmp = farCamera.gameObject.GetComponent<scatterPostprocess> ();
			if (tmp == null) {
				farCamera.gameObject.AddComponent(typeof(scatterPostprocess));
				//tmp = farCamera.gameObject.GetComponent<scatterPostprocess> ();
			}
			//Component.Destroy(cams[cam].gameObject.GetComponent<scatterPostprocess>());				
				//cams[cam+1].gameObject.AddComponent(typeof(scatterPostprocess));
			postprocessingEnabled = true;
		}
		
		public void disablePostprocess(){
			scatterPostprocess tmp = farCamera.gameObject.GetComponent<scatterPostprocess> ();
			if (tmp != null) {
				Component.Destroy (tmp);

			}


			//Component.Destroy(cams[cam+1].gameObject.GetComponent<scatterPostprocess>());
			postprocessingEnabled = false;
		}


		public void SetPostProcessExposure(float postExposure) {
			postProcessExposure=postExposure;
		}
		
		public void SetPostProcessDepth(float postDepth) {
			postProcessDepth=postDepth;
		}
		
		public void SetPostProcessAlpha(float postAlpha) {
			postProcessingAlpha=postAlpha;
		}

		public void SetPostProcessScale(float postScale) {
			postProcessingScale=postScale;
		}

		public void SetAtmosphereGlobalScale(float gScale) {
			atmosphereGlobalScale=gScale;
		}

		public void SetParentCelestialBody(CelestialBody inPlanet) {
			parentCelestialBody=inPlanet;
		}

		public void toggleSunglare()
		{
			if (sunglareEnabled) {
				m_skyMaterial.SetTexture ("_Sun_Glare", black);
				sunglareEnabled = false;
				alphaCutoff=0.2f;
			} 
			else 
			{
				m_skyMaterial.SetTexture("_Sun_Glare", sunGlare);
				sunglareEnabled=true;
				alphaCutoff=0.001f;

			
			}
		}

		

		public RenderTexture getInscatter()
		{
			return m_inscatter;
		}
	
	}
}
