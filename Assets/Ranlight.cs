using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ranlight : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {

		transform.eulerAngles = new Vector3(
			Random.Range(0f, 90f),
			0,
			0
		);
        // this.material.color = new Color(
        // 	Random.Range(0f, 255f),
        // 	Random.Range(0f, 255f), 
        // 	Random.Range(0f, 255f)
        // );
    }
}
