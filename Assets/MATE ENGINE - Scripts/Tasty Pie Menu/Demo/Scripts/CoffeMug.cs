using UnityEngine;
using System.Collections;

public class CoffeMug : MonoBehaviour {

    [SerializeField]
    private GameObject coffeeMesh = null;

    public bool isFilled { get { return coffeeMesh.activeSelf; } }

	public void SetMugFilled(bool state)
    {
        coffeeMesh.SetActive(state);
    }
}
