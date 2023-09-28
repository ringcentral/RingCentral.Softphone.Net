# RingCentral.Softphone.Net

RingCentral Softphone SDK for .NET

This SDK helps you to create a SIP device using SIP over TCP + RTP. WebRTC is NOT used here.


## How to setup

Create a `.env` file in folder "RingCentral.Softphone.Demo", with the following content:

```
SIP_INFO_DOMAIN=sip.ringcentral.com
SIP_INFO_USERNAME=
SIP_INFO_PASSWORD=
SIP_INFO_OUTBOUND_PROXY=sip112-1241.ringcentral.com:5091
SIP_INFO_AUTHORIZATION_ID=
```

For `SIP_INFO_DOMAIN` and `SIP_INFO_OUTBOUND_PROXY`, just specify the values as shown above.

For `SIP_INFO_USERNAME`, it is your RingCentral direct number, like `16501234567`. It is the number that you can make a call to test.

For `SIP_INFO_PASSWORD` and `SIP_INFO_AUTHORIZATION_ID`, you will need to find them in your RingCentral online account.
Please refer to this article: https://medium.com/ringcentral-developers/setup-zoiper-as-a-ringcentral-device-ad484a81d317

Make sure that the `.env` file will be copied to output folder upon build. You need to configure it in Visual Studio or JetBrains Rider.


## Screenshot

![screenshot](./screenshot.png)


## Ref 

- https://github.com/sipsorcery-org/sipsorcery/issues/239
