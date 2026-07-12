using UnityEngine;

public class TankInput : MonoBehaviour
{
    [SerializeField] private bool invertTurnWhenReversing = true;

    public TankInputData ReadInput()
    {
        float moveAxis = Input.GetAxis("Vertical");
        float turnAxis = Input.GetAxis("Horizontal");

        if (invertTurnWhenReversing && moveAxis < 0)
        {
            turnAxis = -turnAxis;
        }

        TankInputData inputData = new TankInputData();
        inputData.MoveAxis = moveAxis;
        inputData.TurnAxis = turnAxis;
        inputData.FirePressed = Input.GetKeyDown(KeyCode.Mouse0);
        inputData.MouseScreenPosition = Input.mousePosition;

        return inputData;
    }
}
