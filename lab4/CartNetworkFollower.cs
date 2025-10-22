using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CartNetworkFollower : MonoBehaviour
{
    [Header("Network → Cart binding")]
    public TCP tcp;           // drag your TCPCompleted here
    public Transform cartRoot;         // drag cart_2w (or a parent) here
    public int cartMarkerId = 5;      // ID you’ll send from Python for the box

    [Header("Smoothing")]
    public float posLerp = 12f;
    public float rotLerp = 12f;
    public bool lockYToGround = true;  // keeps cart on ground plane
    public float groundY = 0f;

    Vector3 targetPos;
    Quaternion targetRot = Quaternion.identity;
    bool haveTarget = false;

    void OnEnable()
    {
        if (tcp != null) tcp.OnMarker += OnMarker;   // hook into TCP callbacks (see below)
    }
    void OnDisable()
    {
        if (tcp != null) tcp.OnMarker -= OnMarker;
    }

    void OnMarker(int id, Vector3 pos)
    {
        if (id != cartMarkerId) return;
        targetPos = pos;
        if (lockYToGround) targetPos.y = groundY;
        haveTarget = true;
    }

    void Update()
    {
        if (!haveTarget || cartRoot == null) return;
        cartRoot.position = Vector3.Lerp(cartRoot.position, targetPos, 1f - Mathf.Exp(-posLerp * Time.deltaTime));
        // keep current rotation unless you decide to send/view yaw; otherwise omit rotation smoothing
    }
}
