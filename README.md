# vrchat-dark-class-generator
DarkClass is to make your custom UdonSharp class used in VRChat


You can call up this tool by clicking <Tools/Nuruwo/DarkClassGenerator> in the Editor menu.
As shown in the image, enter these items and press the button to generate the code.

<kbd><img src="ReadMeImages/usage_example.png" alt="" width="700"/></kbd><br><br>

Generated class can be used in other UdonSharp scripts as follows.

```cs
using UdonSharp;
using UnityEngine;

namespace Nuruwo.Dev
{
    public class DarkClassTest : UdonSharpBehaviour
    {
        void Start()
        {
            //Instantiate
            var myDarkClass = MyDarkClass.New("Jane Doe", 23, new Vector3(0.5f, 0.4f, 0.9f));

            //Get
            Debug.Log(myDarkClass.Name());      //"Jane Doe"
            Debug.Log(myDarkClass.Age());       //23
            Debug.Log(myDarkClass.Position());  //(0.50, 0.40, 0.90)

            //Set
            myDarkClass.Name("Strong Power");
            Debug.Log(myDarkClass.Name());      //"Strong Power"
        }
    }
}
```

See detail technical article
https://power-of-tech.hatenablog.com/entry/2024/06/12/191828