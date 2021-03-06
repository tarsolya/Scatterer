//The only way to get my atmosphere to draw behind the planet was to use a regular meshrenderer because using PostRender in drawSky 
//causes it to be drawn after the whole scene is done
//

using UnityEngine;
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
	
	public class updateAtCameraRythm : MonoBehaviour
	{
		Mesh m_mesh;
		Manager m_manager;
		SkyNode m_skynode;
		GameObject skyObject, skyExtinctObject;
		
		Transform parentTransform;
		Transform celestialTransform;
		
		bool debug6;
		
		public Material skyMat,skyExtinct;
		
		
		
		public void settings(Mesh inmesh,Material inSkyMat, Material inSkyExtinct, Manager inManager, SkyNode inSkyNode, GameObject inSkyObject,GameObject inSkyExtinctObject, bool indebug6, Transform inparentTransform, Transform inCelestialTransform)
		{
			
			skyMat = inSkyMat;
			skyExtinct = inSkyExtinct;
			m_manager = inManager;
			m_skynode = inSkyNode;
			skyObject = inSkyObject;
			skyExtinctObject = inSkyExtinctObject;
			debug6 = indebug6;
			parentTransform = inparentTransform;
			celestialTransform = inCelestialTransform;
			m_mesh = inmesh;
			
		}
		
		
//		public void OnRender()
		public void OnPreRender()
		{
			
			skyMat.SetMatrix ("_Sun_WorldToLocal", m_manager.GetSunWorldToLocalRotation ()); //don't touch this
			
			//			if (debug6){
			skyObject.transform.parent = parentTransform;
			skyExtinctObject.transform.parent = parentTransform;
			//			}
			//			
			//			else{
			//				Transform celestialTransform = ScaledSpace.Instance.scaledSpaceTransforms.Single(t => t.name == parentCelestialBody.name);
			//				tester.transform.parent = celestialTransform;
			//			}
			
			
			m_skynode.InitUniforms(skyMat);
			m_skynode.SetUniforms (skyMat);

			m_skynode.InitUniforms(skyExtinct);
			m_skynode.SetUniforms (skyExtinct);

			m_skynode.UpdateStuff ();
			//			skyMat.SetPass(0);
			//
			//			Graphics.DrawMeshNow(m_mesh, position, Quaternion.identity);
			//			//			Graphics.DrawMesh(m_mesh, position, Quaternion.identity,m_skyMaterial,layer,cam);

			m_mesh.bounds = new Bounds(celestialTransform.position, new Vector3(1e32f,1e32f, 1e32f));

//			skyMat.SetPass(0);
//			Graphics.DrawMesh (m_mesh, celestialTransform.position, Quaternion.identity, skyMat, 10, m_skynode.scaledSpaceCamera);//, 0, null, false, false);
//
//			skyMat.SetPass(0);
//			Graphics.DrawMeshNow(m_mesh, celestialTransform.position, Quaternion.identity);
			
		}		
	}
}