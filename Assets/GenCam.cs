using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GenCam : MonoBehaviour
{
	GameObject car;
	float car_head;
	string[] goArray;

	// Start is called before the first frame update
	void Start()
	{
		// Debug.Log("Start");
		
		Application.targetFrameRate = 1;
		goArray = new string[10];

		goArray[0]="Free_Racing_Car_Blue";
		goArray[1]="Free_Racing_Car_Red";
		goArray[2]="Free_Racing_Car_Gray";
		goArray[3]="Free_Racing_Car_Carbon";
		goArray[4]="Free_Racing_Car_Yellow";
		
		goArray[5]="Policecar";
		goArray[6]="Car1";
		goArray[7]="Car2";

		goArray[8]="Bus";
		goArray[9]="Truck1";
		
		// PrintAllCar();
		car = GameObject.Find(goArray[0]);
		HideAll();
		

		
	    // this.transform.position = new Vector3(0, 0, 0);
	}
	void PrintAllCar() {
		for(int i = 0; i < goArray.Length; i++)
		{
			if (goArray[i]==null) {
				continue;
			}
			Debug.Log(GameObject.Find(goArray[i]).name);
		}
	}
	// Update is called once per frame
	void Update()
	{
		// Debug.Log("Update");
		// Debug.Log("ini "+car.name);
		ReloadCar();
		d3_coor();
	}

	void Show(string gon) {
		GameObject go = GameObject.Find(gon);
		if (go == null) {
			Debug.Log("Null GameObject "+gon);
			return;
		}
		// Debug.Log("Show "+go.name);
		go.transform.position = new Vector3(
			go.transform.position.x,
			go.transform.position.y,
			0
		);
	}
	void Hide(string gon) {
		GameObject go = GameObject.Find(gon);
		if (go == null) {
			Debug.Log("Null GameObject "+gon);
			return;
		}
		// Debug.Log("Hide "+go.name);
		go.transform.position = new Vector3(
			go.transform.position.x,
			go.transform.position.y,
			20
		);
		// Debug.Log(": "+go.transform.position.x.ToString());
		
	}
	void HideAll() {
		for(int i = 0; i < goArray.Length; i++)
		{
			Hide(goArray[i]);
		}
	}
	void ReloadCar() {
		car_head = 1f;
		HideAll();
		int index = Random.Range(0,goArray.Length-1);
		if (car_head>8) {
			car_head+=4;
		}
		car = GameObject.Find(goArray[index]);
		Show(goArray[index]);
	}

	void d3_coor() {
		float cx = car.transform.position.x;
		float cy = car.transform.position.y;
		float cz = car.transform.position.z + car_head;
		
		// cz = cz + car_head;
		
		
		// +left -right
		float x = cx+Random.Range(-3f, 3f);
		// +up -down
		float y = cy+Random.Range(-3f, 3f);
		// +back -front
		float z = cz+Random.Range(3f, 6f);
		// Debug.Log("Update "+" "+x+"-"+y+"-"+z);
		transform.position = new Vector3(x,y,z);
		transform.LookAt(new Vector3(cx,cy,cz));

		Vector3 screenPos = Camera.main.WorldToScreenPoint(car.transform.position);

	}

}
