﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunicaptionBackend.Wrappers;

namespace CommunicaptionBackend.Api {

    public interface IMainService {

        string GeneratePin();

        byte[] GetMediaData(string mediaId);

        public void PushMessage(Message message);

        public void DisconnectDevice(string userId);

        public string CheckForPairing(string pin);

        public string ConnectWithoutHoloLens();
        
        public string ConnectWithHoloLens(string pin);
    }
}