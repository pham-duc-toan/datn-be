#!/bin/sh
# Tao data view "crowd-logs*" trong Kibana de dev vao Discover la tra log duoc ngay.
until curl -sf http://kibana:5601/api/status >/dev/null; do sleep 3; done
curl -s -X POST http://kibana:5601/api/data_views/data_view \
  -H 'kbn-xsrf: true' -H 'Content-Type: application/json' \
  -d '{"data_view":{"id":"crowd-logs","title":"crowd-logs*","name":"Crowd logs","timeFieldName":"@timestamp"},"override":true}'
echo
echo "Da tao data view crowd-logs*"
