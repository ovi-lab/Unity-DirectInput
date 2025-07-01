using UnityEngine;

namespace DirectInputManager
{
    public class Singleton<T> : MonoBehaviour where T: MonoBehaviour
    {

        public static bool verbose = true;

        private static T _instance;
        public static T Instance {
            get {
                if(_instance == null)
                {
                    _instance = FindAnyObjectByType<T>();
                    if(_instance == null)
                    {
                        var singletonObj = new GameObject();
                        singletonObj.name = typeof(T).ToString();
                        _instance = singletonObj.AddComponent<T>();
                    }
                }
                return _instance;
            }
        }

        static public bool isInstanceAlive => _instance != null;

        public virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                if(verbose)
                    Debug.Log("SingleAccessPoint, Destroy duplicate instance " + name + " of " + Instance.name);
                Destroy(gameObject);
                return;
            }

            _instance = GetComponent<T>();


            if (_instance == null)
            {
                if(verbose)
                    Debug.LogError("SingleAccessPoint<" + typeof(T).Name + "> Instance null in Awake");
                return;
            }

            if(verbose)
                Debug.Log("SingleAccessPoint instance found " + Instance.GetType().Name);

        }

    }
}