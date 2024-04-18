#!/bin/bash
echo "Init kms key"
awslocal kms create-key --tags '[{"TagKey":"_custom_id_","TagValue":"37215C9B-650D-4F73-9ED2-1741203CA803"}]'
